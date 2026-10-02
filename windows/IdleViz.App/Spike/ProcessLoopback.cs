#pragma warning disable
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IdleViz.App.Spike;

/// <summary>SPIKE: WASAPI process loopback on one process tree, hand-written interop.</summary>
internal sealed class ProcessLoopback
{
    private const int Size = 1 << 16;
    private readonly float[] _left = new float[Size];
    private readonly float[] _right = new float[Size];
    private long _written;
    private readonly object _gate = new();
    private Thread? _thread;
    private volatile bool _stop;

    public int SampleRate { get; private set; } = 48000;
    public long Packets;
    public long Frames;
    public long SilentPackets;
    public string Format = "";

    public void Start(uint processId, bool includeTree = true, bool floatFormat = true)
    {
        _thread = new Thread(() => Run(processId, includeTree, floatFormat)) { IsBackground = true, Name = "loopback" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop() => _stop = true;

    /// <summary>Copies the newest samples; returns how many frames were written in total.</summary>
    public long Latest(float[] left, float[] right)
    {
        lock (_gate)
        {
            var n = left.Length;
            for (var i = 0; i < n; i++)
            {
                var index = _written - n + i;
                left[i] = index < 0 ? 0 : _left[index & (Size - 1)];
                right[i] = index < 0 ? 0 : _right[index & (Size - 1)];
            }

            return _written;
        }
    }

    private void Run(uint processId, bool includeTree, bool floatFormat)
    {
        try
        {
            var client = Activate(processId, includeTree);
            var format = new WAVEFORMATEX
            {
                wFormatTag = (ushort)(floatFormat ? 3 : 1),
                nChannels = 2,
                nSamplesPerSec = 48000,
                wBitsPerSample = (ushort)(floatFormat ? 32 : 16),
            };
            format.nBlockAlign = (ushort)(format.nChannels * format.wBitsPerSample / 8);
            format.nAvgBytesPerSec = format.nSamplesPerSec * format.nBlockAlign;
            const uint Loopback = 0x00020000, EventCallback = 0x00040000, AutoConvert = 0x80000000, SrcQuality = 0x08000000;
            var hr = client.Initialize(0, Loopback | EventCallback | AutoConvert | SrcQuality, 200000, 0, ref format, IntPtr.Zero);
            Log.Info("audio", $"Initialize pid={processId} tree={includeTree} float={floatFormat}: 0x{hr:X8}");
            if (hr != 0)
            {
                if (floatFormat) { Run(processId, includeTree, false); }
                return;
            }

            Format = floatFormat ? "float32 48k stereo" : "pcm16 48k stereo";
            SampleRate = 48000;
            hr = client.GetBufferSize(out var bufferFrames);
            Log.Info("audio", $"GetBufferSize: 0x{hr:X8} frames={bufferFrames}");
            hr = client.GetMixFormat(out var mix);
            Log.Info("audio", $"GetMixFormat: 0x{hr:X8}");
            hr = client.GetStreamLatency(out var latency);
            Log.Info("audio", $"GetStreamLatency: 0x{hr:X8} {latency / 10000.0} ms");
            using var ready = new AutoResetEvent(false);
            hr = client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
            Log.Info("audio", $"SetEventHandle: 0x{hr:X8}");
            var iid = new Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
            hr = client.GetService(ref iid, out var service);
            Log.Info("audio", $"GetService(capture): 0x{hr:X8}");
            var capture = (IAudioCaptureClient)service;
            hr = client.Start();
            Log.Info("audio", $"Start: 0x{hr:X8}");
            var firstData = Stopwatch.StartNew();
            var first = true;
            while (!_stop)
            {
                ready.WaitOne(200);
                while (capture.GetNextPacketSize(out var packet) == 0 && packet > 0)
                {
                    hr = capture.GetBuffer(out var data, out var frames, out var flags, out _, out _);
                    if (hr != 0) { Log.Info("audio", $"GetBuffer 0x{hr:X8}"); break; }
                    if (first) { first = false; Log.Info("audio", $"First packet after {firstData.ElapsedMilliseconds} ms: {frames} frames, flags {flags}"); }
                    Write(data, (int)frames, (flags & 2) != 0, floatFormat);
                    Packets++;
                    Frames += frames;
                    if ((flags & 2) != 0) { SilentPackets++; }
                    capture.ReleaseBuffer(frames);
                }
            }

            client.Stop();
        }
        catch (Exception error)
        {
            Log.Info("audio", $"Loopback failed: {error}");
        }
    }

    private unsafe void Write(IntPtr data, int frames, bool silent, bool floatFormat)
    {
        lock (_gate)
        {
            for (var i = 0; i < frames; i++)
            {
                float l = 0, r = 0;
                if (!silent)
                {
                    if (floatFormat) { var p = (float*)data; l = p[i * 2]; r = p[(i * 2) + 1]; }
                    else { var p = (short*)data; l = p[i * 2] / 32768f; r = p[(i * 2) + 1] / 32768f; }
                }

                _left[_written & (Size - 1)] = l;
                _right[_written & (Size - 1)] = r;
                _written++;
            }
        }
    }

    private static IAudioClient Activate(uint processId, bool includeTree)
    {
        var parameters = new AUDIOCLIENT_ACTIVATION_PARAMS { ActivationType = 1, TargetProcessId = processId, ProcessLoopbackMode = includeTree ? 0 : 1 };
        var size = Marshal.SizeOf<AUDIOCLIENT_ACTIVATION_PARAMS>();
        var blob = Marshal.AllocHGlobal(size);
        var variant = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.StructureToPtr(parameters, blob, false);
            for (var i = 0; i < 24; i++) { Marshal.WriteByte(variant, i, 0); }
            Marshal.WriteInt16(variant, 0, 65); // VT_BLOB
            Marshal.WriteInt32(variant, 8, size);
            Marshal.WriteIntPtr(variant, 16, blob);
            var handler = new Handler();
            var iid = new Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
            var hr = ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, variant, handler, out var operation);
            Log.Info("audio", $"ActivateAudioInterfaceAsync: 0x{hr:X8}");
            Marshal.ThrowExceptionForHR(hr);
            if (!handler.Done.WaitOne(5000)) { throw new TimeoutException("activation did not complete"); }
            operation.GetActivateResult(out var result, out var unknown);
            Log.Info("audio", $"Activate result: 0x{result:X8}");
            Marshal.ThrowExceptionForHR(result);
            return (IAudioClient)unknown;
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
            Marshal.FreeHGlobal(variant);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AUDIOCLIENT_ACTIVATION_PARAMS
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag;
        public ushort nChannels;
        public uint nSamplesPerSec;
        public uint nAvgBytesPerSec;
        public ushort nBlockAlign;
        public ushort wBitsPerSample;
        public ushort cbSize;
    }

    [DllImport("Mmdevapi.dll", ExactSpelling = true)]
    private static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation activationOperation);

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, ref WAVEFORMATEX format, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint bufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint padding);
        [PreserveSig] int IsFormatSupported(int shareMode, ref WAVEFORMATEX format, out IntPtr closest);
        [PreserveSig] int GetMixFormat(out IntPtr format);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr handle);
        [PreserveSig] int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong devicePosition, out ulong qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint frames);
        [PreserveSig] int GetNextPacketSize(out uint frames);
    }

    private sealed class Handler : IActivateAudioInterfaceCompletionHandler
    {
        public ManualResetEvent Done { get; } = new(false);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation) => Done.Set();
    }
}

using System.Runtime.InteropServices;
using IdleViz.Core;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Media.Audio;
using Windows.Win32.System.Com;
using Windows.Win32.System.Com.StructuredStorage;
using Windows.Win32.System.Diagnostics.ToolHelp;
using Windows.Win32.System.Variant;

namespace IdleViz.App;

/// <summary>
/// Captures Spotify's audio, and only Spotify's: WASAPI process loopback on Spotify's process tree.
/// It is the one audio source the visuals ever get, never the whole system, an input device or the
/// microphone. When Spotify isn't running, nothing is captured and the ring holds silence. Runs on
/// its own thread. Built on what the W2 spike measured; plays the part of <c>SpotifyAudioTap.swift</c>.
/// </summary>
internal sealed class SpotifyCapture : IDisposable
{
    /// <summary>The format asked for. Process loopback can't report one (<c>GetMixFormat</c> is not implemented) but converts to this.</summary>
    public const int SampleRate = 48_000;

    private const int Channels = 2;

    // Ask again this often while Spotify isn't running, and after the capture failed.
    private static readonly TimeSpan s_retry = TimeSpan.FromSeconds(2);

    private readonly Lock _lock = new();
    private Thread? _thread;
    private ManualResetEvent? _stop;
    private int _users;
    private volatile bool _capturing;

    public SampleRing Ring { get; } = new();

    /// <summary>True while Spotify is running and its audio is being captured.</summary>
    public bool IsCapturing => _capturing;

    /// <summary>
    /// Raised on the capture thread with the error text when the capture couldn't be started, and
    /// with null when it started. Spotify not running is not a problem.
    /// </summary>
    public event Action<string?>? ProblemChanged;

    /// <summary>
    /// Checks whether Spotify's audio could be captured right now, without capturing any: null if
    /// it could or Spotify isn't running, else the error text. Call from a thread-pool thread.
    /// </summary>
    public static string? Probe()
    {
        if (SpotifyProcess.Root(Processes()) is not { } processId)
        {
            return null;
        }

        try
        {
            Marshal.ReleaseComObject(Open(processId));
            return null;
        }
        catch (Exception error)
        {
            return StillRunning(processId) ? error.Message : null;
        }
    }

    /// <summary>Starts the capture for one more user: the open window, or Detect delay. It runs while anyone uses it.</summary>
    public void Retain()
    {
        lock (_lock)
        {
            if (_users++ > 0)
            {
                return;
            }

            var stop = new ManualResetEvent(false);
            _stop = stop;
            // Process loopback and its completion handler want a multithreaded apartment.
            _thread = new Thread(() => Run(stop)) { IsBackground = true, Name = "Spotify capture", Priority = ThreadPriority.AboveNormal };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }
    }

    /// <summary>Ends one use. The capture stops with its last user.</summary>
    public void Release()
    {
        Thread? thread;
        ManualResetEvent? stop;
        lock (_lock)
        {
            if (_users == 0 || --_users > 0)
            {
                return;
            }

            thread = _thread;
            stop = _stop;
            _thread = null;
            _stop = null;
        }

        if (thread is null || stop is null)
        {
            return;
        }

        stop.Set();
        if (!thread.Join(TimeSpan.FromSeconds(2)))
        {
            Log.Info("audio", "The capture thread didn't stop within 2 s");
        }

        stop.Dispose();
        // Frames built after this would otherwise repeat the last samples.
        Ring.Clear();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _users = Math.Min(_users, 1);
        }

        Release();
    }

    private void Run(ManualResetEvent stop)
    {
        int? missingLogged = null;
        while (!stop.WaitOne(0))
        {
            var root = SpotifyProcess.Root(Processes());
            if (root is not { } processId)
            {
                if (missingLogged != 0)
                {
                    Log.Info("audio", "Spotify isn't running; sending silence");
                    missingLogged = 0;
                }

                stop.WaitOne(s_retry);
                continue;
            }

            missingLogged = null;
            try
            {
                Capture(processId, stop);
            }
            catch (Exception error)
            {
                Log.Info("audio", $"Capturing Spotify ({processId}) failed: {error.Message}");
                // Spotify quitting in the middle of the attempt fails too, and is no problem.
                if (StillRunning(processId))
                {
                    ProblemChanged?.Invoke(error.Message);
                }

                stop.WaitOne(s_retry);
            }
            finally
            {
                _capturing = false;
                Ring.Clear();
            }
        }
    }

    // Captures until asked to stop or until the Spotify process goes away.
    private void Capture(int processId, ManualResetEvent stop)
    {
        using var process = System.Diagnostics.Process.GetProcessById(processId);
        var client = Open(processId);
        try
        {
            using var ready = new AutoResetEvent(false);
            client.SetEventHandle(new HANDLE(ready.SafeWaitHandle.DangerousGetHandle()));
            object service;
            unsafe
            {
                var iid = typeof(IAudioCaptureClient).GUID;
                client.GetService(&iid, out service);
            }

            var capture = (IAudioCaptureClient)service;
            client.Start();
            _capturing = true;
            Log.Info("audio", $"Capturing Spotify ({processId}) at {SampleRate} Hz");
            ProblemChanged?.Invoke(null);
            WaitHandle[] handles = [stop, ready];
            var lastCheck = Environment.TickCount64;
            while (true)
            {
                var signalled = WaitHandle.WaitAny(handles, 200);
                if (signalled == 0)
                {
                    break;
                }

                Drain(capture);
                if (Environment.TickCount64 - lastCheck >= 1000)
                {
                    lastCheck = Environment.TickCount64;
                    if (process.HasExited)
                    {
                        Log.Info("audio", $"Spotify ({processId}) quit");
                        break;
                    }
                }
            }

            client.Stop();
            Marshal.ReleaseComObject(capture);
        }
        finally
        {
            Marshal.ReleaseComObject(client);
        }
    }

    private unsafe void Drain(IAudioCaptureClient capture)
    {
        capture.GetNextPacketSize(out var packet);
        while (packet > 0)
        {
            capture.GetBuffer(out var data, out var frames, out var flags);
            // When the packet is handed over, not when its samples are stamped: the visuals are
            // delayed from the moment they get the audio, so Detect delay measures from there too.
            var time = AudioClock.Now;
            if ((flags & (uint)_AUDCLNT_BUFFERFLAGS.AUDCLNT_BUFFERFLAGS_SILENT) != 0)
            {
                Ring.AppendSilence((int)frames, time);
            }
            else
            {
                Ring.Append(new ReadOnlySpan<float>(data, (int)frames * Channels), Channels, (int)frames, time);
            }

            capture.ReleaseBuffer(frames);
            capture.GetNextPacketSize(out packet);
        }
    }

    private static bool StillRunning(int processId) => SpotifyProcess.Root(Processes()) == processId;

    // A capture client for Spotify's process tree, set up but not started.
    private static IAudioClient Open(int processId)
    {
        var client = Activate(processId);
        try
        {
            var format = new WAVEFORMATEX
            {
                wFormatTag = 3, // WAVE_FORMAT_IEEE_FLOAT
                nChannels = Channels,
                nSamplesPerSec = SampleRate,
                wBitsPerSample = 32,
                nBlockAlign = Channels * 4,
                nAvgBytesPerSec = SampleRate * Channels * 4,
            };
            const uint Flags = PInvoke.AUDCLNT_STREAMFLAGS_LOOPBACK | PInvoke.AUDCLNT_STREAMFLAGS_EVENTCALLBACK
                | PInvoke.AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM | PInvoke.AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY;
            unsafe
            {
                // A 20 ms buffer; packets come every 10 ms.
                client.Initialize(AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED, Flags, 200_000, 0, &format, null);
            }

            return client;
        }
        catch
        {
            Marshal.ReleaseComObject(client);
            throw;
        }
    }

    private static unsafe IAudioClient Activate(int processId)
    {
        // The parameters must outlive the asynchronous activation, so they live in native memory until it completes.
        var parameters = (AUDIOCLIENT_ACTIVATION_PARAMS*)NativeMemory.AllocZeroed((nuint)sizeof(AUDIOCLIENT_ACTIVATION_PARAMS));
        try
        {
            parameters->ActivationType = AUDIOCLIENT_ACTIVATION_TYPE.AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
            parameters->ProcessLoopbackParams.TargetProcessId = (uint)processId;
            parameters->ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE.PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
            var variant = default(PROPVARIANT);
            variant.vt = VARENUM.VT_BLOB;
            variant.blob = new BLOB { cbSize = (uint)sizeof(AUDIOCLIENT_ACTIVATION_PARAMS), pBlobData = (byte*)parameters };

            using var handler = new ActivationHandler();
            PInvoke.ActivateAudioInterfaceAsync(
                PInvoke.VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, typeof(IAudioClient).GUID, variant, handler, out var operation).ThrowOnFailure();
            if (!handler.Done.WaitOne(TimeSpan.FromSeconds(5)))
            {
                throw new TimeoutException("Windows didn't answer the capture request within 5 s");
            }

            HRESULT result;
            operation.GetActivateResult(&result, out var activated);
            Marshal.ReleaseComObject(operation);
            result.ThrowOnFailure();
            return (IAudioClient)activated;
        }
        finally
        {
            NativeMemory.Free(parameters);
        }
    }

    private static List<ProcessEntry> Processes()
    {
        var list = new List<ProcessEntry>();
        using var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);
        if (snapshot.IsInvalid)
        {
            return list;
        }

        var entry = new PROCESSENTRY32W { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32W>() };
        for (var more = PInvoke.Process32FirstW(snapshot, ref entry); more; more = PInvoke.Process32NextW(snapshot, ref entry))
        {
            list.Add(new ProcessEntry((int)entry.th32ProcessID, (int)entry.th32ParentProcessID, entry.szExeFile.ToString()));
        }

        return list;
    }

    // Windows calls this from one of its own threads when the capture client is ready.
    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler, IDisposable
    {
        public ManualResetEvent Done { get; } = new(false);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation) => Done.Set();

        public void Dispose() => Done.Dispose();
    }
}

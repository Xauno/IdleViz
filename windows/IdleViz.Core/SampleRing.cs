namespace IdleViz.Core;

/// <summary>
/// Keeps the most recent stereo samples from the Spotify capture. Written on the capture thread
/// and read on the UI thread, so everything goes through one lock. Ported from <c>SampleRing.swift</c>.
/// </summary>
public sealed class SampleRing
{
    public const int Capacity = 4096;

    private readonly Lock _lock = new();
    private readonly float[] _left = new float[Capacity];
    private readonly float[] _right = new float[Capacity];
    private int _head;
    private int _buffers;
    private int _zeroBuffers;

    // Mono samples kept for Detect delay while it runs, or null.
    private float[]? _recording;
    private int _recorded;

    // When the first recorded buffer arrived.
    private double _recordingStart;

    /// <summary>Adds one buffer of interleaved samples. Mono is copied to both sides; channels past the second are ignored.</summary>
    /// <param name="interleaved">The samples, frame by frame.</param>
    /// <param name="channels">Samples per frame.</param>
    /// <param name="frames">How many frames.</param>
    /// <param name="time">When the buffer arrived, in seconds, for Detect delay.</param>
    public void Append(ReadOnlySpan<float> interleaved, int channels, int frames, double time = 0)
    {
        channels = Math.Max(channels, 1);
        lock (_lock)
        {
            BeginBuffer(time);
            var allZero = true;
            for (var frame = 0; frame < frames; frame++)
            {
                var left = interleaved[frame * channels];
                var right = channels > 1 ? interleaved[(frame * channels) + 1] : left;
                if (left != 0 || right != 0)
                {
                    allZero = false;
                }

                Store(left, right);
            }

            _buffers++;
            if (allZero)
            {
                _zeroBuffers++;
            }
        }
    }

    /// <summary>Adds one buffer of silence: Windows flags a packet as silent instead of filling it with zeros.</summary>
    public void AppendSilence(int frames, double time = 0)
    {
        lock (_lock)
        {
            BeginBuffer(time);
            for (var frame = 0; frame < frames; frame++)
            {
                Store(0, 0);
            }

            _buffers++;
            _zeroBuffers++;
        }
    }

    /// <summary>Starts keeping every sample (as mono), up to <paramref name="maxSamples"/>, next to the usual ring.</summary>
    public void StartRecording(int maxSamples)
    {
        lock (_lock)
        {
            // Allocated up front, so the capture thread never has to grow it.
            _recording = new float[Math.Max(maxSamples, 0)];
            _recorded = 0;
            _recordingStart = 0;
        }
    }

    /// <summary>Stops recording and returns the samples and the time at which the first of them arrived.</summary>
    public (float[] Samples, double Start) StopRecording()
    {
        lock (_lock)
        {
            var samples = _recording is null ? [] : _recording.AsSpan(0, _recorded).ToArray();
            _recording = null;
            _recorded = 0;
            return (samples, _recordingStart);
        }
    }

    /// <summary>Copies the newest <c>left.Length</c> samples of each channel, oldest first.</summary>
    public void Latest(float[] left, float[] right)
    {
        var count = Math.Min(Math.Min(left.Length, right.Length), Capacity);
        lock (_lock)
        {
            var index = (_head - count + Capacity) % Capacity;
            for (var offset = 0; offset < count; offset++)
            {
                left[offset] = _left[index];
                right[offset] = _right[index];
                index = (index + 1) % Capacity;
            }
        }
    }

    /// <summary>Without this, frames built after the capture stops would repeat the last samples forever.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            Array.Clear(_left);
            Array.Clear(_right);
        }
    }

    /// <summary>Buffers received since the last call, and how many of them held only exact zeros.</summary>
    public (int Buffers, int Zero) TakeCounts()
    {
        lock (_lock)
        {
            var counts = (_buffers, _zeroBuffers);
            _buffers = 0;
            _zeroBuffers = 0;
            return counts;
        }
    }

    private void BeginBuffer(double time)
    {
        if (_recording is not null && _recorded == 0)
        {
            _recordingStart = time;
        }
    }

    private void Store(float left, float right)
    {
        _left[_head] = left;
        _right[_head] = right;
        _head = (_head + 1) % Capacity;
        if (_recording is not null && _recorded < _recording.Length)
        {
            _recording[_recorded++] = (left + right) / 2;
        }
    }
}

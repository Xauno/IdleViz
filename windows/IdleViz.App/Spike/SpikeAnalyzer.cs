#pragma warning disable
using System.Buffers.Binary;
using System.Numerics;

namespace IdleViz.App.Spike;

/// <summary>SPIKE: a rough analysis, only good enough to drive the page. W7a ports the real one.</summary>
internal sealed class SpikeAnalyzer
{
    public const int N = 1024;
    public const int FrameLength = 24 + (64 * 4) + (N * 4) + (3 * N);
    private readonly float[] _left = new float[N];
    private readonly float[] _right = new float[N];
    private readonly float[] _bands = new float[64];
    private readonly Complex[] _fft = new Complex[N];
    private readonly byte[] _frame = new byte[FrameLength];
    private uint _sequence;

    public float Rms { get; private set; }

    public float Peak { get; private set; }

    public float[] Left => _left;

    public float[] Right => _right;

    public string Frame(int sampleRate)
    {
        double sum = 0;
        float peak = 0;
        for (var i = 0; i < N; i++)
        {
            var mono = (_left[i] + _right[i]) / 2;
            sum += mono * mono;
            peak = Math.Max(peak, Math.Abs(mono));
            var window = 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (N - 1)));
            _fft[i] = new Complex(mono * window, 0);
        }

        Rms = (float)Math.Sqrt(sum / N);
        Peak = peak;
        Fft(_fft);
        for (var b = 0; b < 64; b++)
        {
            var low = 40 * Math.Pow(16000.0 / 40, b / 64.0);
            var high = 40 * Math.Pow(16000.0 / 40, (b + 1) / 64.0);
            var first = (int)Math.Floor(low * N / sampleRate);
            var last = Math.Max(first, (int)Math.Floor(high * N / sampleRate));
            double best = 0;
            for (var k = Math.Max(1, first); k <= Math.Min(N / 2, last); k++) { best = Math.Max(best, _fft[k].Magnitude / (N / 4.0)); }
            var db = 20 * Math.Log10(Math.Max(best, 1e-9));
            var target = (float)Math.Clamp((db + 65) / 55, 0, 1);
            _bands[b] += (target - _bands[b]) * (target > _bands[b] ? 0.7f : 0.12f);
        }

        var span = _frame.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, _sequence++);
        BinaryPrimitives.WriteSingleLittleEndian(span[4..], sampleRate);
        BinaryPrimitives.WriteSingleLittleEndian(span[8..], Average(0, 8));
        BinaryPrimitives.WriteSingleLittleEndian(span[12..], Average(8, 30));
        BinaryPrimitives.WriteSingleLittleEndian(span[16..], Average(30, 64));
        BinaryPrimitives.WriteSingleLittleEndian(span[20..], Math.Min(1, Rms));
        for (var b = 0; b < 64; b++) { BinaryPrimitives.WriteSingleLittleEndian(span[(24 + (b * 4))..], _bands[b]); }
        for (var i = 0; i < N; i++)
        {
            var mono = (_left[i] + _right[i]) / 2;
            BinaryPrimitives.WriteSingleLittleEndian(span[(280 + (i * 4))..], mono);
            _frame[4376 + i] = ToByte(mono);
            _frame[4376 + N + i] = ToByte(_left[i]);
            _frame[4376 + (2 * N) + i] = ToByte(_right[i]);
        }

        return Convert.ToBase64String(_frame);
    }

    private static byte ToByte(float sample) => (byte)Math.Clamp(128 + (sample * 127), 0, 255);

    private float Average(int from, int to)
    {
        float sum = 0;
        for (var i = from; i < to; i++) { sum += _bands[i]; }
        return sum / (to - from);
    }

    private static void Fft(Complex[] data)
    {
        var n = data.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) { j ^= bit; }
            j ^= bit;
            if (i < j) { (data[i], data[j]) = (data[j], data[i]); }
        }

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2 * Math.PI / len;
            var wlen = new Complex(Math.Cos(angle), Math.Sin(angle));
            for (var i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (var j = 0; j < len / 2; j++)
                {
                    var u = data[i + j];
                    var v = data[i + j + (len / 2)] * w;
                    data[i + j] = u + v;
                    data[i + j + (len / 2)] = u - v;
                    w *= wlen;
                }
            }
        }
    }
}

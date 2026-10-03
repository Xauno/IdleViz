using System.Buffers.Binary;

namespace IdleViz.Core;

/// <summary>
/// Everything the page needs for one frame of visuals, computed from Spotify's audio only.
/// Ported from <c>AudioFrame.swift</c>.
/// </summary>
public sealed class AudioFrame
{
    public const int SampleCount = 1024;
    public const int BandCount = 64;
    public const int HeaderLength = 24;

    /// <summary>24-byte header, 64 bands and a 1024-sample waveform as <c>f32</c>, then three 1024-byte arrays.</summary>
    public const int ByteLength = HeaderLength + (BandCount * 4) + (SampleCount * 4) + (3 * SampleCount);

    public uint Sequence { get; set; }

    public float SampleRate { get; set; }

    /// <summary>Average of bands 0–7, 0 to 1.</summary>
    public float Bass { get; set; }

    /// <summary>Average of bands 8–29, 0 to 1.</summary>
    public float Mid { get; set; }

    /// <summary>Average of bands 30–63, 0 to 1.</summary>
    public float Treble { get; set; }

    /// <summary>Loudness after automatic gain, 0 to 1.</summary>
    public float Rms { get; set; }

    /// <summary>64 log-spaced bands from about 40 Hz to 16 kHz, 0 to 1, noise-floored and smoothed.</summary>
    public float[] Bands { get; } = new float[BandCount];

    /// <summary>Mono samples after automatic gain, −1 to 1.</summary>
    public float[] Waveform { get; } = new float[SampleCount];

    /// <summary>Butterchurn's input: time-domain samples as unsigned bytes (128 is silence).</summary>
    public byte[] MonoBytes { get; } = Silent();

    public byte[] LeftBytes { get; } = Silent();

    public byte[] RightBytes { get; } = Silent();

    /// <summary>A frame of silence.</summary>
    public static AudioFrame Silence(uint sequence, float sampleRate) => new() { Sequence = sequence, SampleRate = sampleRate };

    /// <summary>
    /// The binary form sent to the page, little-endian. <c>web/audio-frame.js</c> unpacks it:
    /// <c>u32</c> sequence, <c>f32</c> sample rate, bass, mid, treble, rms, <c>f32[64]</c> bands,
    /// <c>f32[1024]</c> waveform, <c>u8[1024]</c> mono, left, right.
    /// </summary>
    public byte[] Packed()
    {
        var data = new byte[ByteLength];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, Sequence);
        var offset = 4;
        foreach (var value in (ReadOnlySpan<float>)[SampleRate, Bass, Mid, Treble, Rms])
        {
            BinaryPrimitives.WriteSingleLittleEndian(span[offset..], value);
            offset += 4;
        }

        foreach (var values in (float[][])[Bands, Waveform])
        {
            foreach (var value in values)
            {
                BinaryPrimitives.WriteSingleLittleEndian(span[offset..], value);
                offset += 4;
            }
        }

        foreach (var bytes in (byte[][])[MonoBytes, LeftBytes, RightBytes])
        {
            bytes.CopyTo(span[offset..]);
            offset += bytes.Length;
        }

        return data;
    }

    /// <summary>
    /// The call that hands a packed frame to the page. Base64 only uses characters that are safe
    /// inside a JavaScript string. The <c>?.</c> keeps a frame sent before the page has loaded from throwing.
    /// </summary>
    public static string Script(byte[] packed) => $"window.audioFrame?.('{Convert.ToBase64String(packed)}')";

    private static byte[] Silent()
    {
        var bytes = new byte[SampleCount];
        Array.Fill(bytes, (byte)128);
        return bytes;
    }
}

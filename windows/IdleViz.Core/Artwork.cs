namespace IdleViz.Core;

/// <summary>
/// The covers of the few most recently played tracks. Spotify hands a cover over a moment after
/// the track itself, so going back to a recent track shows its cover at once.
/// </summary>
public sealed class ArtworkCache
{
    // Most recently used last.
    private readonly List<(string Id, byte[] Data)> _entries = [];

    public ArtworkCache(int capacity = 5)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public IReadOnlyList<string> Ids => [.. _entries.Select(entry => entry.Id)];

    /// <summary>The cover stored for a track (see <see cref="NowPlaying.Id"/>), or null.</summary>
    public byte[]? Image(string id)
    {
        var index = _entries.FindIndex(entry => entry.Id == id);
        if (index < 0)
        {
            return null;
        }

        var entry = _entries[index];
        _entries.RemoveAt(index);
        _entries.Add(entry);
        return entry.Data;
    }

    public void Insert(byte[] data, string id)
    {
        _entries.RemoveAll(entry => entry.Id == id);
        _entries.Add((id, data));
        if (_entries.Count > Capacity)
        {
            _entries.RemoveRange(0, _entries.Count - Capacity);
        }
    }
}

public static class Artwork
{
    /// <summary>Larger than any cover Spotify sends (they are around 100 KB). Anything bigger is not read.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>The image type by its first bytes: JPEG, PNG or WebP. Null for anything else.</summary>
    public static string? MimeType(ReadOnlySpan<byte> data)
    {
        if (data.StartsWith<byte>([0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (data.StartsWith<byte>([0x89, 0x50, 0x4E, 0x47]))
        {
            return "image/png";
        }

        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>
    /// A <c>data:</c> URL for JPEG, PNG or WebP bytes, which is how a cover travels to the page
    /// (the page never uses the network). Null for anything else.
    /// </summary>
    public static string? DataUrl(ReadOnlySpan<byte> data)
    {
        var type = MimeType(data);
        return type is null ? null : $"data:{type};base64,{Convert.ToBase64String(data)}";
    }
}

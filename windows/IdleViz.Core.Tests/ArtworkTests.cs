namespace IdleViz.Core.Tests;

public class ArtworkTests
{
    private static readonly byte[] s_jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2];
    private static readonly byte[] s_png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] s_webp = [.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8, 1];

    [Fact]
    public void KnowsJpegPngAndWebpByTheirFirstBytes()
    {
        Assert.Equal("image/jpeg", Artwork.MimeType(s_jpeg));
        Assert.Equal("image/png", Artwork.MimeType(s_png));
        Assert.Equal("image/webp", Artwork.MimeType(s_webp));
    }

    [Fact]
    public void RejectsEverythingElse()
    {
        Assert.Null(Artwork.MimeType([]));
        Assert.Null(Artwork.MimeType([0xFF, 0xD8]));
        Assert.Null(Artwork.MimeType("GIF89a"u8));
        Assert.Null(Artwork.MimeType("<svg xmlns="u8));
        // RIFF, but a WAV file.
        Assert.Null(Artwork.MimeType([.. "RIFF"u8, 0, 0, 0, 0, .. "WAVE"u8]));
        Assert.Null(Artwork.MimeType("RIFF"u8));
    }

    [Fact]
    public void MakesADataUrl()
    {
        Assert.Equal("data:image/jpeg;base64,/9j/4AEC", Artwork.DataUrl(s_jpeg));
        Assert.StartsWith("data:image/png;base64,", Artwork.DataUrl(s_png));
        Assert.Null(Artwork.DataUrl("not an image"u8));
    }
}

public class ArtworkCacheTests
{
    [Fact]
    public void KeepsTheMostRecentlyUsed()
    {
        var cache = new ArtworkCache(capacity: 2);
        cache.Insert([1], "a");
        cache.Insert([2], "b");
        // Using "a" makes "b" the oldest.
        Assert.Equal([1], cache.Image("a"));
        cache.Insert([3], "c");
        Assert.Equal(["a", "c"], cache.Ids);
        Assert.Null(cache.Image("b"));
        Assert.Equal([3], cache.Image("c"));
    }

    [Fact]
    public void ANewCoverForTheSameTrackReplacesTheOld()
    {
        var cache = new ArtworkCache(capacity: 2);
        cache.Insert([1], "a");
        cache.Insert([2], "b");
        cache.Insert([9], "a");
        Assert.Equal(["b", "a"], cache.Ids);
        Assert.Equal([9], cache.Image("a"));
    }

    [Fact]
    public void HoldsFiveByDefault()
    {
        var cache = new ArtworkCache();
        for (var i = 0; i < 7; i++)
        {
            cache.Insert([(byte)i], $"track {i}");
        }

        Assert.Equal(5, cache.Capacity);
        Assert.Equal(["track 2", "track 3", "track 4", "track 5", "track 6"], cache.Ids);
    }

    [Fact]
    public void NeedsRoomForAtLeastOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArtworkCache(capacity: 0));
    }
}

using System.Text.Json;

namespace IdleViz.Core.Tests;

public class PageStatusTests
{
    [Fact]
    public void ReadsAStatusReply()
    {
        var reply = """{"preset":"bundled:Geiss - Swirl","frames":1200,"audioFrames":1190,"presets":40,"failed":[{"id":"custom:bad.json","error":"Unexpected token"}]}""";
        var status = PageStatus.FromReply(reply)!;
        Assert.Equal("bundled:Geiss - Swirl", status.Preset);
        Assert.Equal(1200, status.Frames);
        Assert.Equal(1190, status.AudioFrames);
        Assert.Equal([new PresetFailure("custom:bad.json", "Unexpected token")], status.Failed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("\"ok\"")]
    [InlineData("[1,2]")]
    [InlineData("{not json")]
    public void RejectsAnythingThatIsNotAnObject(string? reply) => Assert.Null(PageStatus.FromReply(reply));

    [Fact]
    public void TreatsTheReplyAsUntrusted()
    {
        var longText = new string('x', 5000);
        var failed = new List<object>
        {
            new { id = longText, error = longText },
            "text",
            3,
            new { error = "no id" },
            new { id = "custom:no-error" },
        };
        failed.AddRange(Enumerable.Repeat<object>(new { id = "custom:f", error = "e" }, 500));
        var reply = JsonSerializer.Serialize(new { preset = longText, frames = 1e300, audioFrames = -5, failed });

        var status = PageStatus.FromReply(reply)!;
        Assert.Equal(PageStatus.MaxTextLength, status.Preset!.Length);
        Assert.Equal(0, status.Frames);
        Assert.Equal(0, status.AudioFrames);
        Assert.Equal(PageStatus.MaxTextLength, status.Failed[0].Id.Length);
        Assert.Equal(PageStatus.MaxTextLength, status.Failed[0].Error.Length);
        Assert.Equal(new PresetFailure("custom:no-error", "Failed to load"), status.Failed[1]);
        Assert.True(status.Failed.Count <= PageStatus.MaxFailures);
    }

    [Fact]
    public void RefusesDeepNesting()
    {
        var deep = string.Concat(Enumerable.Repeat("[", 50)) + string.Concat(Enumerable.Repeat("]", 50));
        Assert.Null(PageStatus.FromReply($$"""{"preset":null,"failed":{{deep}}}"""));
    }

    [Fact]
    public void MissingFieldsReadAsEmpty()
    {
        var status = PageStatus.FromReply("{}")!;
        Assert.Null(status.Preset);
        Assert.Equal(0, status.Frames);
        Assert.Empty(status.Failed);
    }

    [Fact]
    public void ComparesFailures()
    {
        var one = PageStatus.FromReply("""{"failed":[{"id":"a","error":"e"}]}""")!;
        Assert.True(one.SameFailures(PageStatus.FromReply("""{"frames":3,"failed":[{"id":"a","error":"e"}]}""")));
        Assert.False(one.SameFailures(PageStatus.FromReply("{}")));
        Assert.False(one.SameFailures(null));
    }
}

public class PageWatchdogTests
{
    [Fact]
    public void ReloadsAfterThreeSecondsWithoutAReply()
    {
        var watchdog = new PageWatchdog();
        watchdog.Start(100);
        Assert.False(watchdog.ShouldReload(101));
        Assert.False(watchdog.ShouldReload(102));
        Assert.True(watchdog.ShouldReload(103));
        // The new page gets a fresh three seconds.
        Assert.False(watchdog.ShouldReload(104));
        Assert.True(watchdog.ShouldReload(106));
    }

    [Fact]
    public void RepliesKeepItQuiet()
    {
        var watchdog = new PageWatchdog();
        watchdog.Start(0);
        for (var second = 1; second <= 20; second++)
        {
            Assert.False(watchdog.ShouldReload(second));
            watchdog.Replied(second + 0.01);
        }
    }

    [Fact]
    public void DoesNothingWhileStopped()
    {
        var watchdog = new PageWatchdog();
        Assert.False(watchdog.ShouldReload(50));
        watchdog.Start(0);
        watchdog.Stop();
        watchdog.Replied(1);
        Assert.False(watchdog.ShouldReload(50));
    }
}

public class CustomPresetPayloadTests
{
    [Fact]
    public void SendsTheHungPresets()
    {
        var payload = new CustomPresetPayload([], ["bundled:Geiss - Swirl"]);
        Assert.Equal("""window.setCustomPresets?.({"entries":[],"hung":["bundled:Geiss - Swirl"]})""", payload.Script);
    }

    [Fact]
    public void EscapesWhatCouldEndTheScript()
    {
        var name = "a'</script>" + (char)0x2028;
        var payload = new CustomPresetPayload([new("custom:x", name, "custom", "preset", "https://presets.idleviz.invalid/x.json", "1")], []);
        var script = payload.Script;
        Assert.DoesNotContain("</script>", script, StringComparison.Ordinal);
        Assert.DoesNotContain('\'', script);
        Assert.DoesNotContain((char)0x2028, script);
        Assert.Contains("\"source\":\"custom\"", script, StringComparison.Ordinal);
    }
}

public class SpotifyProcessTests
{
    [Fact]
    public void PicksTheTopOfSpotifysTree()
    {
        ProcessEntry[] processes =
        [
            new(4, 0, "System"),
            new(900, 600, "explorer.exe"),
            new(1200, 900, "Spotify.exe"),
            new(1300, 1200, "Spotify.exe"),
            new(1310, 1200, "Spotify.exe"),
            new(1400, 1300, "Spotify.exe"),
            new(2000, 900, "msedge.exe"),
        ];
        Assert.Equal(1200, SpotifyProcess.Root(processes));
    }

    [Fact]
    public void NoneWithoutSpotify() => Assert.Null(SpotifyProcess.Root([new(900, 600, "explorer.exe")]));

    [Fact]
    public void MatchesTheNameInAnyCase() => Assert.Equal(50, SpotifyProcess.Root([new(50, 7, "SPOTIFY.EXE")]));

    [Fact]
    public void PrefersTheRootWithChildren()
    {
        // A left-over helper whose parent ended, next to the running app.
        ProcessEntry[] processes = [new(100, 1, "Spotify.exe"), new(300, 2, "Spotify.exe"), new(310, 300, "Spotify.exe"), new(320, 300, "Spotify.exe")];
        Assert.Equal(300, SpotifyProcess.Root(processes));
    }
}

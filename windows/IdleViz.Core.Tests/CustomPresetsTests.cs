using System.Text;

namespace IdleViz.Core.Tests;

public sealed class PresetFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "IdleVizPresets-" + Guid.NewGuid().ToString("N"));

    public PresetFolderTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private void Touch(string relative, string text = "x")
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void FindsPresetsPluginsAndMilkInSubfolders()
    {
        Touch("Tunnel.json");
        Touch("pack/Glow.MILK");
        Touch("plugins/aurora.js");
        Touch("notes.txt");
        Touch("pack/readme.md");

        var files = PresetFolder.Scan(_root);
        Assert.Equal(["pack/Glow.MILK", "plugins/aurora.js", "Tunnel.json"], files.Select(file => file.RelativePath));
        Assert.Equal([CustomPresetKind.Milk, CustomPresetKind.Plugin, CustomPresetKind.Json], files.Select(file => file.Kind));
        Assert.Equal("custom:pack/Glow.MILK", files[0].Id);
        Assert.Equal("Glow", files[0].Name);
    }

    [Fact]
    public void SkipsHiddenFilesAndFolders()
    {
        Touch(".cache/abc.json");
        Touch(".hidden.json");
        Touch("hidden/inside.json");
        File.SetAttributes(Path.Combine(_root, "hidden"), FileAttributes.Directory | FileAttributes.Hidden);
        Touch("shown.json");
        Touch("secret.json");
        File.SetAttributes(Path.Combine(_root, "secret.json"), FileAttributes.Hidden);

        Assert.Equal(["shown.json"], PresetFolder.Scan(_root).Select(file => file.RelativePath));
    }

    [Fact]
    public void TheVersionChangesWithTheFile()
    {
        Touch("a.json", "one");
        var before = PresetFolder.Scan(_root)[0].Version;
        Touch("a.json", "longer");
        Assert.NotEqual(before, PresetFolder.Scan(_root)[0].Version);
    }

    [Fact]
    public void AMissingFolderIsEmpty() => Assert.Empty(PresetFolder.Scan(Path.Combine(_root, "nope")));

    [Fact]
    public void StopsAtTheLimit()
    {
        for (var i = 0; i < PresetFolder.MaxFiles + 3; i++)
        {
            File.WriteAllText(Path.Combine(_root, $"{i}.json"), "x");
        }

        Assert.Equal(PresetFolder.MaxFiles, PresetFolder.Scan(_root).Count);
    }

    [Fact]
    public void SortsAsFileExplorerDoes()
    {
        string[] names = ["b10.json", "B2.json", "a.json", "b1.json"];
        Assert.Equal(["a.json", "b1.json", "B2.json", "b10.json"], names.Order(Comparer<string>.Create(PresetFolder.NaturalCompare)));
    }

    [Fact]
    public void NameKeepsALeadingDot()
    {
        Assert.Equal(".json", new CustomPresetFile(".json", CustomPresetKind.Json, "1").Name);
        Assert.Equal("a.b", new CustomPresetFile("x/a.b.json", CustomPresetKind.Json, "1").Name);
    }
}

public sealed class PluginMetaTests
{
    [Theory]
    [InlineData("export const meta = { name: \"Aurora Ring\" };", "Aurora Ring")]
    [InlineData("export const meta = {\n  author: 'me',\n  name: 'Tunnel'\n}", "Tunnel")]
    [InlineData("export   const meta={name:\"  Spaced  \"}", "Spaced")]
    public void ReadsTheDeclaredName(string source, string name) => Assert.Equal(name, PluginMeta.Name(source));

    [Theory]
    [InlineData("const meta = { name: \"Not exported\" }")]
    [InlineData("export const meta = { name: someVariable }")]
    [InlineData("export const meta = { name: \"\" }")]
    [InlineData("export const meta = { name: \"   \" }")]
    [InlineData("export function init() {}")]
    public void NoNameWithoutAPlainString(string source) => Assert.Null(PluginMeta.Name(source));

    [Fact]
    public void ALongNameIsNotRead() => Assert.Null(PluginMeta.Name($"export const meta = {{ name: \"{new string('n', 101)}\" }}"));
}

public sealed class PresetImportTests
{
    [Fact]
    public void AFreeNameIsKept() => Assert.Equal("Tunnel.milk", PresetImport.FreeName("Tunnel.milk", isFolder: false, _ => false));

    [Fact]
    public void ATakenNameGetsANumberAsFileExplorerGivesIt()
    {
        var taken = new HashSet<string> { "Tunnel.milk", "Tunnel (2).milk" };
        Assert.Equal("Tunnel (3).milk", PresetImport.FreeName("Tunnel.milk", isFolder: false, taken.Contains));
    }

    [Fact]
    public void AFolderNameKeepsItsDots()
    {
        var taken = new HashSet<string> { "pack.v2" };
        Assert.Equal("pack.v2 (2)", PresetImport.FreeName("pack.v2", isFolder: true, taken.Contains));
    }

    [Fact]
    public void ANameWithoutAnExtension()
    {
        var taken = new HashSet<string> { ".hidden", "plain" };
        Assert.Equal(".hidden (2)", PresetImport.FreeName(".hidden", isFolder: false, taken.Contains));
        Assert.Equal("plain (2)", PresetImport.FreeName("plain", isFolder: false, taken.Contains));
    }
}

public sealed class PresetImportCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "IdleVizImport-" + Guid.NewGuid().ToString("N"));

    private string Source => Path.Combine(_root, "source");

    private string Presets => Path.Combine(_root, "Presets");

    public PresetImportCopyTests() => Directory.CreateDirectory(Path.Combine(Source, "pack", "inner"));

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void CopiesFilesAndWholeFolders()
    {
        File.WriteAllText(Path.Combine(Source, "Tunnel.milk"), "milk");
        File.WriteAllText(Path.Combine(Source, "pack", "a.json"), "a");
        File.WriteAllText(Path.Combine(Source, "pack", "inner", "b.js"), "b");

        var (copied, problems) = PresetImport.CopyInto(Presets, [Path.Combine(Source, "Tunnel.milk"), Path.Combine(Source, "pack")]);
        Assert.Equal(2, copied);
        Assert.Empty(problems);
        Assert.Equal("milk", File.ReadAllText(Path.Combine(Presets, "Tunnel.milk")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(Presets, "pack", "inner", "b.js")));
        Assert.Equal(["pack/a.json", "pack/inner/b.js", "Tunnel.milk"], PresetFolder.Scan(Presets).Select(file => file.RelativePath));
    }

    [Fact]
    public void NothingIsReplaced()
    {
        File.WriteAllText(Path.Combine(Source, "Tunnel.milk"), "new");
        File.WriteAllText(Path.Combine(Source, "pack", "a.json"), "new");
        Directory.CreateDirectory(Path.Combine(Presets, "pack"));
        File.WriteAllText(Path.Combine(Presets, "Tunnel.milk"), "old");
        File.WriteAllText(Path.Combine(Presets, "pack", "a.json"), "old");

        var (copied, _) = PresetImport.CopyInto(Presets, [Path.Combine(Source, "Tunnel.milk"), Path.Combine(Source, "pack") + Path.DirectorySeparatorChar]);
        Assert.Equal(2, copied);
        Assert.Equal("old", File.ReadAllText(Path.Combine(Presets, "Tunnel.milk")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Presets, "Tunnel (2).milk")));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Presets, "pack", "a.json")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Presets, "pack (2)", "a.json")));
    }

    [Fact]
    public void AMissingSourceIsReportedAndTheRestCopied()
    {
        File.WriteAllText(Path.Combine(Source, "ok.json"), "ok");
        var (copied, problems) = PresetImport.CopyInto(Presets, [Path.Combine(Source, "gone.json"), Path.Combine(Source, "ok.json")]);
        Assert.Equal(1, copied);
        Assert.StartsWith("gone.json: ", Assert.Single(problems), StringComparison.Ordinal);
    }
}

public sealed class MilkConversionTests
{
    private const string Preset = """{"baseVals":{"decay":0.98},"shapes":[],"waves":[],"warp":"","comp":""}""";

    [Fact]
    public void TheCacheKeyMatchesTheMacs()
    {
        // SHA-256 of "milkdrop-preset-converter 0.1.2" followed by "abc", as CryptoKit computes it.
        var expected = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(MilkConversion.ConverterVersion + "abc")));
        Assert.Equal(expected, MilkConversion.CacheKey(Encoding.UTF8.GetBytes("abc")));
        Assert.Equal(64, expected.Length);
        Assert.NotEqual(MilkConversion.CacheKey([1]), MilkConversion.CacheKey([2]));
        Assert.Equal($".cache/{expected}.json", MilkConversion.CachePath(expected));
    }

    [Fact]
    public void TextFallsBackToLatin1()
    {
        Assert.Equal("fDecay=0.98 é", MilkConversion.Text(Encoding.UTF8.GetBytes("fDecay=0.98 é")));
        Assert.Equal("café", MilkConversion.Text([0x63, 0x61, 0x66, 0xE9]));
        Assert.Equal("[preset00]", MilkConversion.Text([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("[preset00]")]));
    }

    [Theory]
    [InlineData("[preset00]\nfRating=3", null)]
    [InlineData("per_frame_1=zoom=1", null)]
    [InlineData("fDecay=0.98", null)]
    [InlineData("hello world", "Not a Milkdrop preset")]
    public void ChecksTheSourceLooksLikeAPreset(string source, string? problem) => Assert.Equal(problem, MilkConversion.Problem(source));

    [Fact]
    public void ATooLargeSourceIsRefused() =>
        Assert.Equal("File is too large", MilkConversion.Problem("[preset00]" + new string('x', MilkConversion.MaxSourceBytes)));

    [Fact]
    public void AcceptsAConvertedPreset() => Assert.Equal((Preset, null), MilkConversion.CheckResult(Preset));

    [Theory]
    [InlineData(null, "The converter returned nothing")]
    [InlineData("not json", "The converter returned something that isn't a preset")]
    [InlineData("[]", "The converter returned something that isn't a preset")]
    [InlineData("""{"baseVals":{},"shapes":[]}""", "The converter returned something that isn't a preset")]
    [InlineData("""{"baseVals":[],"shapes":[],"waves":[]}""", "The converter returned something that isn't a preset")]
    [InlineData("""{"baseVals":{},"shapes":[],"waves":[],"warp":"// parsing failed here"}""", "The warp shader couldn't be converted")]
    [InlineData("""{"baseVals":{},"shapes":[],"waves":[],"comp":"parsing failed"}""", "The comp shader couldn't be converted")]
    public void RefusesWhatIsntAPreset(string? reply, string error) => Assert.Equal((null, error), MilkConversion.CheckResult(reply));

    [Fact]
    public void RefusesATooLargeResult()
    {
        var big = $$"""{"baseVals":{},"shapes":[],"waves":[],"pad":"{{new string('x', MilkConversion.MaxResultBytes)}}"}""";
        Assert.Equal((null, "The converted preset is too large"), MilkConversion.CheckResult(big));
    }
}

public sealed class CustomPresetEntryTests
{
    [Fact]
    public void EntriesPointWhereThePageLoadsThem()
    {
        var json = new CustomPresetFile("pack/My Tunnel.json", CustomPresetKind.Json, "10-5");
        var entry = CustomPresetPayload.Entry.ForFile(json);
        Assert.Equal(new CustomPresetPayload.Entry("custom:pack/My Tunnel.json", "My Tunnel", "custom", "preset", "https://presets.idleviz.invalid/pack/My%20Tunnel.json", "10-5"), entry);

        var plugin = new CustomPresetFile("glow.js", CustomPresetKind.Plugin, "1-1");
        Assert.Equal("Glow Ring", CustomPresetPayload.Entry.ForFile(plugin, "Glow Ring").Name);
        Assert.Equal("glow", CustomPresetPayload.Entry.ForFile(plugin).Name);
        Assert.Equal("plugin", CustomPresetPayload.Entry.ForFile(plugin).Kind);

        var milk = new CustomPresetFile("a.milk", CustomPresetKind.Milk, "2-2");
        Assert.Equal("https://presets.idleviz.invalid/.cache/k.json", CustomPresetPayload.Entry.ForMilk(milk, "k").Url);

        var bundled = CustomPresetPayload.Entry.Bundled("aurora.js", "Aurora Ring");
        Assert.Equal(new CustomPresetPayload.Entry("bundled:visuals/aurora.js", "Aurora Ring", "bundled", "plugin", "https://app.idleviz.invalid/visuals/aurora.js", "bundled"), bundled);
    }

    [Fact]
    public void TheCachedConversionIsServed()
    {
        var root = Path.Combine(Path.GetTempPath(), "IdleVizPresets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, ".cache"));
        try
        {
            File.WriteAllText(Path.Combine(root, ".cache", "k.json"), "{}");
            Assert.NotNull(AppAddresses.PresetFile(CustomPresetPayload.Entry.ForMilk(new CustomPresetFile("a.milk", CustomPresetKind.Milk, "1"), "k").Url, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}

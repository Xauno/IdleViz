namespace IdleViz.Core.Tests;

public sealed class AppAddressesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "IdleVizTests", Guid.NewGuid().ToString("N"));
    private readonly string _web;
    private readonly string _presets;

    public AppAddressesTests()
    {
        _web = Directory.CreateDirectory(Path.Combine(_folder, "web")).FullName;
        _presets = Directory.CreateDirectory(Path.Combine(_folder, "Presets")).FullName;
        Directory.CreateDirectory(Path.Combine(_web, "visuals"));
        File.WriteAllText(Path.Combine(_web, "index.html"), "<!doctype html>");
        File.WriteAllText(Path.Combine(_web, "visuals", "aurora.js"), "//");
        File.WriteAllText(Path.Combine(_folder, "secret.txt"), "outside");
        File.WriteAllText(Path.Combine(_presets, "Tunnel (2).json"), "{}");
        File.WriteAllText(Path.Combine(_presets, "glow.js"), "//");
        File.WriteAllText(Path.Combine(_presets, "notes.txt"), "not a preset");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void ServesTheAppsFiles()
    {
        Assert.Equal(Path.Combine(_web, "index.html"), AppAddresses.FileFor(AppAddresses.PageUrl, _web));
        Assert.Equal(Path.Combine(_web, "visuals", "aurora.js"), AppAddresses.FileFor("https://app.idleviz.invalid/visuals/aurora.js", _web));
        // Case, a query and a fragment don't matter.
        Assert.NotNull(AppAddresses.FileFor("https://APP.idleviz.invalid/index.html?v=1#top", _web));
    }

    [Theory]
    [InlineData("https://app.idleviz.invalid/")]
    [InlineData("https://app.idleviz.invalid/missing.js")]
    [InlineData("https://app.idleviz.invalid/visuals")]
    [InlineData("http://app.idleviz.invalid/index.html")]
    [InlineData("https://app.idleviz.invalid:8443/index.html")]
    [InlineData("https://user@app.idleviz.invalid/index.html")]
    [InlineData("https://presets.idleviz.invalid/index.html")]
    [InlineData("https://example.com/index.html")]
    [InlineData("idleviz-app://app/index.html")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("index.html")]
    [InlineData("")]
    [InlineData(null)]
    public void RefusesOtherPlacesAndMissingFiles(string? url) => Assert.Null(AppAddresses.FileFor(url, _web));

    [Theory]
    [InlineData("https://app.idleviz.invalid/../secret.txt")]
    [InlineData("https://app.idleviz.invalid/%2e%2e/secret.txt")]
    [InlineData("https://app.idleviz.invalid/%2E%2E%2Fsecret.txt")]
    [InlineData("https://app.idleviz.invalid/..%5Csecret.txt")]
    [InlineData("https://app.idleviz.invalid/visuals/%2e%2e%5c..%5csecret.txt")]
    [InlineData("https://app.idleviz.invalid/C:%5CWindows%5Cwin.ini")]
    [InlineData("https://app.idleviz.invalid/index.html::$DATA")]
    [InlineData("https://app.idleviz.invalid/index.html.")]
    [InlineData("https://app.idleviz.invalid/index.html%20")]
    [InlineData("https://app.idleviz.invalid/index%00.html")]
    public void NeverLeavesTheFolder(string url) => Assert.Null(AppAddresses.FileFor(url, _web));

    [Fact]
    public void RefusesAFileReachedThroughALink()
    {
        var link = Path.Combine(_web, "outside");
        try
        {
            Directory.CreateSymbolicLink(link, _folder);
        }
        catch (IOException)
        {
            // Making a symbolic link needs Developer Mode or administrator rights; a junction doesn't.
            Assert.Skip("Can't make a symbolic link here.");
        }
        catch (UnauthorizedAccessException)
        {
            Assert.Skip("Can't make a symbolic link here.");
        }

        Assert.True(File.Exists(Path.Combine(link, "secret.txt")));
        Assert.Null(AppAddresses.FileFor("https://app.idleviz.invalid/outside/secret.txt", _web));
    }

    [Fact]
    public void ServesPresetsAndPluginsOnly()
    {
        Assert.Equal(Path.Combine(_presets, "Tunnel (2).json"), AppAddresses.PresetFile("https://presets.idleviz.invalid/Tunnel%20(2).json", _presets));
        Assert.Equal(Path.Combine(_presets, "glow.js"), AppAddresses.PresetFile("https://presets.idleviz.invalid/glow.js", _presets));
        Assert.Null(AppAddresses.PresetFile("https://presets.idleviz.invalid/notes.txt", _presets));
        Assert.Null(AppAddresses.PresetFile("https://app.idleviz.invalid/glow.js", _presets));
        Assert.Null(AppAddresses.PresetFile("https://presets.idleviz.invalid/../web/index.html", _presets));
    }

    [Fact]
    public void PresetAddressesAreEncodedAndComeBackToTheFile()
    {
        var url = AppAddresses.PresetUrl("Tunnel (2).json");
        Assert.Equal("https://presets.idleviz.invalid/Tunnel%20%282%29.json", url);
        Assert.Equal(Path.Combine(_presets, "Tunnel (2).json"), AppAddresses.PresetFile(url, _presets));
        Assert.Equal("https://presets.idleviz.invalid/sub/a%23b%3F.json", AppAddresses.PresetUrl("sub\\a#b?.json"));
    }

    [Fact]
    public void EachPageGetsItsOwnPolicy()
    {
        Assert.Equal(AppAddresses.ContentSecurityPolicy, AppAddresses.ContentSecurityPolicyFor("index.html"));
        Assert.Equal(AppAddresses.PluginFrameContentSecurityPolicy, AppAddresses.ContentSecurityPolicyFor(@"C:\x\plugin-host.html"));
        Assert.Equal(AppAddresses.ConverterContentSecurityPolicy, AppAddresses.ContentSecurityPolicyFor("converter.html"));
    }

    // The Mac's policies with idleviz-app: swapped for the two hosts, and nothing else.
    [Fact]
    public void PoliciesMatchTheMacs()
    {
        Assert.Equal(
            "default-src 'none'; script-src 'self' 'unsafe-eval'; style-src 'self'; font-src 'self'; img-src 'self' data: blob:; connect-src 'self' https://presets.idleviz.invalid; frame-src 'self'",
            AppAddresses.ContentSecurityPolicy);
        Assert.Equal(
            "default-src 'none'; script-src https://app.idleviz.invalid https://presets.idleviz.invalid; img-src data: blob:",
            AppAddresses.PluginFrameContentSecurityPolicy);
        Assert.Equal("default-src 'none'; script-src 'self' 'unsafe-eval'", AppAddresses.ConverterContentSecurityPolicy);
    }

    [Theory]
    [InlineData("index.html", "text/html; charset=utf-8")]
    [InlineData("a/b.JS", "text/javascript; charset=utf-8")]
    [InlineData("overlay.css", "text/css; charset=utf-8")]
    [InlineData("x.json", "application/json")]
    [InlineData("font.ttf", "font/ttf")]
    [InlineData("tool.exe", "application/octet-stream")]
    public void KnowsTheTypes(string file, string type) => Assert.Equal(type, AppAddresses.MimeType(file));

    [Theory]
    [InlineData("https://app.idleviz.invalid/index.html", true)]
    [InlineData("https://app.idleviz.invalid/plugin-host.html", true)]
    [InlineData("https://presets.idleviz.invalid/glow.js", false)]
    [InlineData("https://example.com/", false)]
    [InlineData("http://app.idleviz.invalid/index.html", false)]
    [InlineData("about:blank", false)]
    [InlineData("idleviz://open", false)]
    public void OnlyTheAppsOwnPagesMayLoad(string url, bool allowed) => Assert.Equal(allowed, AppAddresses.IsAppPage(url));
}

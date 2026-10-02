#pragma warning disable
using System.Diagnostics;
using System.Globalization;
using Microsoft.UI.Dispatching;

namespace IdleViz.App.Spike;

/// <summary>
/// SPIKE (W2, never merged). Started with --spike. Switches:
///   --spike-root=PATH      the web folder (default: IdleViz\web found above the exe)
///   --spike-origins=LIST   AllowedOrigins for the custom scheme (default *)
///   --spike-opacity=0..1   window opacity after the page loads (default 1)
///   --spike-plugin         show the bundled Aurora plugin (sandboxed frame) instead of Butterchurn
///   --spike-pid=N          capture this process instead of looking for Spotify
///   --spike-notree         capture only that process, not its children
///   --spike-nopage / --spike-noaudio / --spike-nomedia / --spike-noframes
///   --spike-seconds=N      exit after N seconds (default 60)
/// </summary>
internal sealed class SpikeRunner
{
    private readonly DispatcherQueue _dispatcher;
    private readonly VisualizerWindow _window;
    private readonly string[] _args;
    private readonly ProcessLoopback _loopback = new();
    private readonly SpikeAnalyzer _analyzer = new();
    private readonly MediaLog _media = new();
    private SpikePage? _page;
    private readonly List<DispatcherQueueTimer> _timers = [];
    private volatile bool _pageReady;
    private int _pending;
    private readonly List<float> _levels = [];
    private long _sent;
    private long _dropped;
    private long _scriptTicks;
    private long _scriptCalls;
    private long _buildTicks;

    public SpikeRunner(DispatcherQueue dispatcher, VisualizerWindow window, string[] args)
    {
        _dispatcher = dispatcher;
        _window = window;
        _args = args;
    }

    private bool Has(string name) => _args.Contains(name);

    private string? Value(string name) => _args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];

    public async void Start()
    {
        try
        {
            Log.Info("spike", $"Start: {string.Join(' ', _args)}");
            if (!Has("--spike-nowindow"))
            {
                _window.SetOpacity(1);
                _window.Show();
            }

            if (!Has("--spike-nomedia"))
            {
                await _media.StartAsync();
            }

            if (!Has("--spike-noaudio"))
            {
                StartAudio();
            }

            if (!Has("--spike-nopage"))
            {
                var root = Value("--spike-root") ?? FindWebRoot();
                Log.Info("spike", $"Web root {root}");
                _page = await SpikePage.CreateAsync(_window.Handle, root, Value("--spike-origins") ?? "*");
                _page.Web.NavigationCompleted += async (_, e) =>
                {
                    if (Has("--spike-plugin"))
                    {
                        var entries = """{"entries":[{"id":"bundled:aurora","name":"Aurora","source":"bundled","kind":"plugin","url":"idleviz-app://app/visuals/aurora.js","version":"1"}],"hung":[]}""";
                        Log.Info("spike", "setCustomPresets -> " + await _page.Web.ExecuteScriptAsync($"window.setCustomPresets?.({entries})"));
                        Log.Info("spike", "setPresetSettings -> " + await _page.Web.ExecuteScriptAsync("""window.setPresetSettings?.({"mode":"single","single":"bundled:aurora"})"""));
                    }

                    var now = """{"id":"spike","state":"playing","content":"song","title":"Spike title","artist":"Spike artist","artwork":null,"artworkPending":false,"durationMs":200000,"position":12.5}""";
                    Log.Info("spike", "nowPlaying -> " + await _page.Web.ExecuteScriptAsync($"window.nowPlaying?.({now})"));
                    Log.Info("spike", "facts -> " + await _page.Web.ExecuteScriptAsync("JSON.stringify({origin: location.origin, secure: isSecureContext, dpr: devicePixelRatio, w: innerWidth, h: innerHeight, butterchurn: typeof butterchurn, ua: navigator.userAgent, gl: (()=>{const c=document.createElement('canvas').getContext('webgl2'); if(!c) return null; const e=c.getExtension('WEBGL_debug_renderer_info'); return e? c.getParameter(e.UNMASKED_RENDERER_WEBGL): 'webgl2';})(), frames: document.querySelectorAll('iframe').length, evalWorks: (()=>{try{return new Function('return 7')()}catch(e){return String(e)}})()})"));
                    _pageReady = true;
                };
            }

            var opacity = double.Parse(Value("--spike-opacity") ?? "1", CultureInfo.InvariantCulture);
            if (opacity < 1)
            {
                var later = _dispatcher.CreateTimer();
                _timers.Add(later); // a timer nobody references is collected and stops
                later.Interval = TimeSpan.FromSeconds(6);
                later.IsRepeating = false;
                later.Tick += (_, _) => { _window.SetOpacity(opacity); Log.Info("spike", $"Opacity {opacity}"); };
                later.Start();
            }

            if (!Has("--spike-noframes"))
            {
                new Thread(Pump) { IsBackground = true, Name = "pump" }.Start();
            }

            var stats = _dispatcher.CreateTimer();
            _timers.Add(stats);
            stats.Interval = TimeSpan.FromSeconds(1);
            stats.Tick += (_, _) => Stats();
            stats.Start();

            var seconds = int.Parse(Value("--spike-seconds") ?? "60", CultureInfo.InvariantCulture);
            var end = _dispatcher.CreateTimer();
            _timers.Add(end);
            end.Interval = TimeSpan.FromSeconds(seconds);
            end.IsRepeating = false;
            end.Tick += (_, _) => { Log.Info("spike", "End"); Environment.Exit(0); };
            end.Start();
        }
        catch (Exception error)
        {
            Log.Info("spike", $"Failed: {error}");
        }
    }

    private static string FindWebRoot()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "IdleViz", "web");
            if (File.Exists(Path.Combine(candidate, "index.html"))) { return candidate; }
        }

        throw new DirectoryNotFoundException("IdleViz\\web");
    }

    private void StartAudio()
    {
        uint pid;
        if (Value("--spike-pid") is { } given)
        {
            pid = uint.Parse(given, CultureInfo.InvariantCulture);
        }
        else
        {
            var all = Process.GetProcessesByName("Spotify");
            foreach (var p in all)
            {
                string start;
                try { start = p.StartTime.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture); } catch (Exception e) { start = e.Message; }
                Log.Info("audio", $"Spotify process {p.Id}: started {start}, window \"{p.MainWindowTitle}\", handle {p.MainWindowHandle}");
            }

            if (all.Length == 0)
            {
                Log.Info("audio", "No Spotify process");
                return;
            }

            // The one with the window; else the oldest.
            var root = all.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero) ?? all.OrderBy(p => p.StartTime).First();
            pid = (uint)root.Id;
        }

        Log.Info("audio", $"Capturing process {pid}, tree={!Has("--spike-notree")}");
        _loopback.Start(pid, includeTree: !Has("--spike-notree"));
    }

    // About 60 times a second: newest samples -> frame -> script call. A frame is dropped, not queued, while one is in flight.
    private void Pump()
    {
        var clock = Stopwatch.StartNew();
        long tick = 0;
        while (true)
        {
            tick++;
            var due = tick * 1000.0 / 60;
            var wait = due - clock.Elapsed.TotalMilliseconds;
            if (wait > 1) { Thread.Sleep((int)wait); }
            while (clock.Elapsed.TotalMilliseconds < due) { Thread.SpinWait(50); }

            var build = Stopwatch.GetTimestamp();
            _loopback.Latest(_analyzer.Left, _analyzer.Right);
            var script = "window.audioFrame?.(\"" + _analyzer.Frame(_loopback.SampleRate) + "\")";
            Interlocked.Add(ref _buildTicks, Stopwatch.GetTimestamp() - build);
            lock (_levels) { _levels.Add(_analyzer.Rms); }
            if (!_pageReady) { continue; }
            if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
            {
                Interlocked.Increment(ref _dropped);
                continue;
            }

            _dispatcher.TryEnqueue(async () =>
            {
                var started = Stopwatch.GetTimestamp();
                try
                {
                    await _page!.Web.ExecuteScriptAsync(script);
                }
                catch (Exception error)
                {
                    Log.Info("spike", $"audioFrame failed: {error.Message}");
                }

                Interlocked.Add(ref _scriptTicks, Stopwatch.GetTimestamp() - started);
                Interlocked.Increment(ref _scriptCalls);
                Interlocked.Increment(ref _sent);
                Volatile.Write(ref _pending, 0);
            });
        }
    }

    private long _lastFrames, _lastAudioFrames, _lastSent, _lastDropped, _lastPackets, _lastCaptured, _lastCalls, _lastScriptTicks, _lastBuildTicks;
    private TimeSpan _lastOwnCpu;
    private readonly Dictionary<int, TimeSpan> _lastBrowserCpu = [];
    private int _second;

    // The level over the whole second, in dB-friendly form: the root of the mean of the squared frame levels.
    private double MeanLevel()
    {
        lock (_levels)
        {
            var mean = _levels.Count == 0 ? 0 : Math.Sqrt(_levels.Sum(v => (double)v * v) / _levels.Count);
            _levels.Clear();
            return mean;
        }
    }

    private async void Stats()
    {
        _second++;
        var ownCpu = Process.GetCurrentProcess().TotalProcessorTime;
        var line = string.Create(CultureInfo.InvariantCulture, $"t={_second}s rms={MeanLevel():F4} peak={_analyzer.Peak:F4} packets/s={_loopback.Packets - _lastPackets} captured/s={_loopback.Frames - _lastCaptured} silentPackets={_loopback.SilentPackets} ownCpu={(ownCpu - _lastOwnCpu).TotalMilliseconds / 10:F1}%");
        _lastOwnCpu = ownCpu;
        _lastPackets = _loopback.Packets;
        _lastCaptured = _loopback.Frames;

        if (_page is not null && _pageReady)
        {
            var calls = Interlocked.Read(ref _scriptCalls);
            var ticks = Interlocked.Read(ref _scriptTicks);
            var buildTicks = Interlocked.Read(ref _buildTicks);
            var perCall = calls > _lastCalls ? (ticks - _lastScriptTicks) * 1000.0 / Stopwatch.Frequency / (calls - _lastCalls) : 0;
            var perBuild = (buildTicks - _lastBuildTicks) * 1000.0 / Stopwatch.Frequency / 60;
            line += string.Create(CultureInfo.InvariantCulture, $" sent/s={_sent - _lastSent} dropped/s={_dropped - _lastDropped} scriptRoundTrip={perCall:F2}ms build={perBuild:F3}ms");
            _lastCalls = calls;
            _lastScriptTicks = ticks;
            _lastBuildTicks = buildTicks;
            _lastSent = _sent;
            _lastDropped = _dropped;

            double browserCpu = 0;
            var kinds = new List<string>();
            foreach (var info in _page.Environment.GetProcessInfos())
            {
                try
                {
                    var cpu = Process.GetProcessById(info.ProcessId).TotalProcessorTime;
                    var delta = _lastBrowserCpu.TryGetValue(info.ProcessId, out var last) ? (cpu - last).TotalMilliseconds / 10 : 0;
                    _lastBrowserCpu[info.ProcessId] = cpu;
                    browserCpu += delta;
                    kinds.Add(string.Create(CultureInfo.InvariantCulture, $"{info.Kind}={delta:F1}%"));
                }
                catch (Exception)
                {
                }
            }

            line += string.Create(CultureInfo.InvariantCulture, $" webviewCpu={browserCpu:F1}% ({string.Join(' ', kinds)})");

            try
            {
                var json = await _page.Web.ExecuteScriptAsync("JSON.stringify(window.idlevizStatus?.() ?? null)");
                var text = System.Text.Json.JsonSerializer.Deserialize<string>(json) ?? "null";
                using var status = System.Text.Json.JsonDocument.Parse(text);
                if (status.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    var frames = status.RootElement.GetProperty("frames").GetInt64();
                    var audioFrames = status.RootElement.GetProperty("audioFrames").GetInt64();
                    line += $" pageFps={frames - _lastFrames} pageAudioFrames/s={audioFrames - _lastAudioFrames} preset={status.RootElement.GetProperty("preset")} presets={status.RootElement.GetProperty("presets")} failed={status.RootElement.GetProperty("failed")}";
                    _lastFrames = frames;
                    _lastAudioFrames = audioFrames;
                }
                else
                {
                    line += " status=null";
                }
            }
            catch (Exception error)
            {
                line += $" status failed: {error.Message}";
            }
        }

        Log.Info("stats", line);
        if (_second % 5 == 0 && !Has("--spike-nomedia"))
        {
            _media.Poll();
        }
    }
}

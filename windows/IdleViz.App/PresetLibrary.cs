using System.Diagnostics;
using IdleViz.Core;
using Microsoft.UI.Dispatching;

namespace IdleViz.App;

/// <summary>
/// The custom presets folder: scans and watches it, converts <c>.milk</c> files once and caches
/// the result, and tells the page what there is to show. Ported from <c>PresetLibrary.swift</c>.
/// Everything here runs on the UI thread; only the reading of the folder happens off it.
/// </summary>
internal sealed class PresetLibrary : IDisposable
{
    private static readonly TimeSpan s_settle = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherQueue _dispatcher;
    private readonly MilkConverter _converter;
    private readonly string _webRoot;
    private readonly DispatcherQueueTimer _settleTimer;
    private FileSystemWatcher? _watcher;
    private bool _scanning;
    private bool _scanAgain;
    private bool _published;
    private CustomPresetPayload _payload = CustomPresetPayload.Empty;

    // Conversions that failed, by cache key, so the same contents aren't tried on every scan.
    private readonly Dictionary<string, string> _failedConversions = new(StringComparer.Ordinal);

    // Presets that were on screen when the page stopped answering, with the version that did it.
    private readonly Dictionary<string, string> _hung = new(StringComparer.Ordinal);
    private Dictionary<string, string> _versions = new(StringComparer.Ordinal);

    // The cache key of each .milk file seen so far, with the version it was computed from, so
    // unchanged files aren't read and hashed again on every scan.
    private Dictionary<string, MilkKey> _milkKeys = new(StringComparer.Ordinal);

    public PresetLibrary(DispatcherQueue dispatcher, string folder, MilkConverter converter)
    {
        _dispatcher = dispatcher;
        Folder = folder;
        _converter = converter;
        _webRoot = Path.Combine(AppContext.BaseDirectory, "web");
        _settleTimer = dispatcher.CreateTimer();
        _settleTimer.Interval = s_settle;
        _settleTimer.IsRepeating = false;
        _settleTimer.Tick += (_, _) => _ = Scan();
    }

    /// <summary>Raised with the full list for the page each time it changes.</summary>
    public event Action<CustomPresetPayload>? PayloadChanged;

    /// <summary>Raised when the count or the conversion failures change, for settings.</summary>
    public event Action? Changed;

    public string Folder { get; }

    /// <summary>How many usable presets and plugins the folder holds.</summary>
    public int CustomCount { get; private set; }

    /// <summary>Files that couldn't be used, with the reason: unreadable files and <c>.milk</c> files that didn't convert.</summary>
    public IReadOnlyList<PresetFailure> ConversionFailures { get; private set; } = [];

    /// <summary>Creates the folder if needed, reads it, and starts watching it.</summary>
    public void Start()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            _watcher = new FileSystemWatcher(Folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Created += OnFileEvent;
            _watcher.Changed += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            // Too many changes at once: the list of them is lost, so read everything again.
            _watcher.Error += (_, _) => _dispatcher.TryEnqueue(FolderChanged);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Log.Info("presets", $"Couldn't create or watch the presets folder: {error.Message}");
        }

        _ = Scan();
    }

    /// <summary>Reads the folder again now.</summary>
    public void Reload()
    {
        _settleTimer.Stop();
        _ = Scan();
    }

    /// <summary>The page stopped answering while this preset was on screen. It stays out until its file changes.</summary>
    public void MarkHung(string id)
    {
        _hung[id] = _versions.GetValueOrDefault(id, string.Empty);
        Log.Info("presets", $"Marked as failed after the page stopped answering: {id}");
        Publish(_payload.Entries);
    }

    /// <summary>Copies files and folders into the presets folder. A name that is taken gets a number; nothing is replaced.</summary>
    public void Import(IEnumerable<string> paths)
    {
        var (copied, problems) = PresetImport.CopyInto(Folder, paths);
        foreach (var problem in problems)
        {
            Log.Info("presets", $"Couldn't import {problem}");
        }

        Log.Info("presets", $"Imported {copied} item(s)");
        Reload();
    }

    public void OpenFolder()
    {
        Directory.CreateDirectory(Folder);
        StartExplorer($"\"{Folder}\"");
    }

    /// <summary>The file behind a custom preset's id, or null for a bundled one.</summary>
    public string? FileFor(string id) =>
        id.StartsWith("custom:", StringComparison.Ordinal)
            ? Path.Combine(Folder, id["custom:".Length..].Replace('/', Path.DirectorySeparatorChar))
            : null;

    /// <summary>Shows the file in File Explorer, selected.</summary>
    public void Reveal(string id)
    {
        if (FileFor(id) is { } file)
        {
            StartExplorer($"/select,\"{file}\"");
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _settleTimer.Stop();
        _converter.Close();
    }

    private static void StartExplorer(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false });
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Info("presets", $"Couldn't open File Explorer: {error.Message}");
        }
    }

    // Changes inside the conversion cache are the app's own.
    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        var relative = Path.GetRelativePath(Folder, e.FullPath);
        if (relative.Split(Path.DirectorySeparatorChar)[0] == PresetFolder.CacheFolderName)
        {
            return;
        }

        _dispatcher.TryEnqueue(FolderChanged);
    }

    // Copying a pack in arrives as many events; wait until they stop.
    private void FolderChanged()
    {
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private async Task Scan()
    {
        if (_scanning)
        {
            _scanAgain = true;
            return;
        }

        _scanning = true;
        try
        {
            var folder = Folder;
            var webRoot = _webRoot;
            var known = _milkKeys;
            var found = await Task.Run(() => Read(folder, webRoot, known));
            _milkKeys = found.Keys;
            _versions = found.Versions;

            var failures = found.Unreadable.Concat(await ConvertMilkFiles(found)).ToList();
            var keep = found.Keys.Values.Select(key => key.Key).ToHashSet(StringComparer.Ordinal);
            await Task.Run(() => RemoveStaleCache(folder, keep));

            // A file that changed since it hung the page gets another chance.
            foreach (var id in _hung.Keys.ToList())
            {
                if (id.StartsWith("custom:", StringComparison.Ordinal) && found.Versions.GetValueOrDefault(id) != _hung[id])
                {
                    _hung.Remove(id);
                }
            }

            ConversionFailures = failures;
            CustomCount = found.Entries.Count(entry => entry.Source == "custom");
            Publish(found.Entries);
            Changed?.Invoke();
        }
        catch (Exception error)
        {
            Log.Info("presets", $"Reading the presets folder failed: {error.Message}");
        }
        finally
        {
            _scanning = false;
            if (_scanAgain)
            {
                _scanAgain = false;
                _ = Scan();
            }
        }
    }

    // Converts the .milk files that have no cached result, adding the ones that work to the scan.
    private async Task<List<PresetFailure>> ConvertMilkFiles(ScanResult scan)
    {
        var failures = new List<PresetFailure>();
        var converted = 0;
        foreach (var item in scan.Unconverted)
        {
            if (_failedConversions.TryGetValue(item.Key, out var known))
            {
                failures.Add(new PresetFailure(item.File.Id, known));
                continue;
            }

            var (json, error) = await _converter.Convert(item.Source);
            if (json is null)
            {
                var message = error ?? "The conversion failed";
                _failedConversions[item.Key] = message;
                failures.Add(new PresetFailure(item.File.Id, message));
                Log.Info("presets", $"Couldn't convert {item.File.RelativePath}: {message}");
                continue;
            }

            try
            {
                var target = Path.Combine(Folder, MilkConversion.CachePath(item.Key).Replace('/', Path.DirectorySeparatorChar));
                var cache = Path.GetDirectoryName(target)!;
                var created = !Directory.Exists(cache);
                Directory.CreateDirectory(cache);
                if (created)
                {
                    // Hidden, as on the Mac where a leading dot hides it.
                    File.SetAttributes(cache, File.GetAttributes(cache) | FileAttributes.Hidden);
                }

                await File.WriteAllTextAsync(target + ".tmp", json);
                File.Move(target + ".tmp", target, overwrite: true);
                scan.Entries.Add(CustomPresetPayload.Entry.ForMilk(item.File, item.Key));
                converted++;
                // A big pack takes a while; hand over what's ready as it goes.
                if (converted % 100 == 0)
                {
                    CustomCount = scan.Entries.Count(entry => entry.Source == "custom");
                    Publish(scan.Entries);
                    Changed?.Invoke();
                }
            }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
            {
                failures.Add(new PresetFailure(item.File.Id, "Couldn't save the converted preset"));
            }
        }

        _converter.Close();
        if (converted > 0)
        {
            Log.Info("presets", $"Converted {converted} Milkdrop preset(s)");
        }

        return failures;
    }

    private void Publish(IReadOnlyList<CustomPresetPayload.Entry> entries)
    {
        var next = new CustomPresetPayload([.. entries], [.. _hung.Keys.Order(StringComparer.Ordinal)]);
        if (_published && next.Script == _payload.Script)
        {
            return;
        }

        _published = true;
        _payload = next;
        Log.Info("presets", $"Presets folder: {CustomCount} custom, {ConversionFailures.Count} failed");
        PayloadChanged?.Invoke(next);
    }

    // Reads the folder off the UI thread: every file, plugin names, and which .milk files are cached.
    private static ScanResult Read(string folder, string webRoot, Dictionary<string, MilkKey> known)
    {
        var scan = new ScanResult();
        scan.Entries.AddRange(BundledPlugins(webRoot));
        bool Cached(string key) => File.Exists(Path.Combine(folder, MilkConversion.CachePath(key).Replace('/', Path.DirectorySeparatorChar)));
        foreach (var file in PresetFolder.Scan(folder))
        {
            var path = Path.Combine(folder, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            scan.Versions[file.Id] = file.Version;
            switch (file.Kind)
            {
                case CustomPresetKind.Json:
                    scan.Entries.Add(CustomPresetPayload.Entry.ForFile(file));
                    break;
                case CustomPresetKind.Plugin:
                    scan.Entries.Add(CustomPresetPayload.Entry.ForFile(file, PluginName(path)));
                    break;
                case CustomPresetKind.Milk:
                    if (known.TryGetValue(file.Id, out var before) && before.Version == file.Version && Cached(before.Key))
                    {
                        scan.Keys[file.Id] = before;
                        scan.Entries.Add(CustomPresetPayload.Entry.ForMilk(file, before.Key));
                        break;
                    }

                    byte[] contents;
                    try
                    {
                        contents = File.ReadAllBytes(path);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        scan.Unreadable.Add(new PresetFailure(file.Id, "Couldn't read the file"));
                        break;
                    }

                    var key = MilkConversion.CacheKey(contents);
                    scan.Keys[file.Id] = new MilkKey(file.Version, key);
                    if (Cached(key))
                    {
                        scan.Entries.Add(CustomPresetPayload.Entry.ForMilk(file, key));
                    }
                    else
                    {
                        scan.Unconverted.Add(new Unconverted(file, key, MilkConversion.Text(contents)));
                    }

                    break;
            }
        }

        return scan;
    }

    /// <summary>The plugins shipped in the app's <c>web\visuals</c> folder.</summary>
    private static List<CustomPresetPayload.Entry> BundledPlugins(string webRoot)
    {
        var visuals = Path.Combine(webRoot, "visuals");
        if (!Directory.Exists(visuals))
        {
            return [];
        }

        return Directory.EnumerateFiles(visuals, "*.js")
            .Order(StringComparer.Ordinal)
            .Select(file => CustomPresetPayload.Entry.Bundled(Path.GetFileName(file), PluginName(file) ?? Path.GetFileNameWithoutExtension(file)))
            .ToList();
    }

    /// <summary>The name a plugin declares, read from the start of its source.</summary>
    private static string? PluginName(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var buffer = new byte[256 * 1024];
            var length = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
            return PluginMeta.Name(System.Text.Encoding.UTF8.GetString(buffer, 0, length));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Deletes cached conversions whose <c>.milk</c> file is gone or changed.</summary>
    private static void RemoveStaleCache(string folder, HashSet<string> keep)
    {
        var cache = Path.Combine(folder, PresetFolder.CacheFolderName);
        if (!Directory.Exists(cache))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(cache, "*.json"))
        {
            if (!keep.Contains(Path.GetFileNameWithoutExtension(file)))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    // Tried again on the next scan.
                }
            }
        }
    }

    private sealed record MilkKey(string Version, string Key);

    private sealed record Unconverted(CustomPresetFile File, string Key, string Source);

    private sealed class ScanResult
    {
        public List<CustomPresetPayload.Entry> Entries { get; } = [];

        public Dictionary<string, string> Versions { get; } = new(StringComparer.Ordinal);

        /// <summary>.milk files with no cached conversion yet.</summary>
        public List<Unconverted> Unconverted { get; } = [];

        public List<PresetFailure> Unreadable { get; } = [];

        /// <summary>The cache key of every .milk file, by id.</summary>
        public Dictionary<string, MilkKey> Keys { get; } = new(StringComparer.Ordinal);
    }
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IdleViz.Core;

public enum CustomPresetKind
{
    /// <summary>A Butterchurn preset (<c>.json</c>).</summary>
    Json,

    /// <summary>A visual plugin (<c>.js</c>, see docs/custom-visualizer.md).</summary>
    Plugin,

    /// <summary>An original Milkdrop preset (<c>.milk</c>), converted before use.</summary>
    Milk,
}

/// <summary>One file in the custom presets folder. Ported from <c>CustomPresets.swift</c>.</summary>
/// <param name="RelativePath">Path inside the presets folder, with forward slashes: <c>pack/Tunnel.milk</c>.</param>
/// <param name="Kind">What the file is, from its extension.</param>
/// <param name="Version">Size and modification time. A different value means the file changed, so a failed one is tried again.</param>
public sealed record CustomPresetFile(string RelativePath, CustomPresetKind Kind, string Version)
{
    public string Id => "custom:" + RelativePath;

    /// <summary>The file name without its extension.</summary>
    public string Name
    {
        get
        {
            var file = RelativePath[(RelativePath.LastIndexOf('/') + 1)..];
            var dot = file.LastIndexOf('.');
            return dot > 0 ? file[..dot] : file;
        }
    }

    public static CustomPresetKind? KindFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".json" => CustomPresetKind.Json,
        ".js" => CustomPresetKind.Plugin,
        ".milk" => CustomPresetKind.Milk,
        _ => null,
    };
}

public static class PresetFolder
{
    /// <summary>More files than this are ignored, so a mistaken import of a huge folder can't stall the app.</summary>
    public const int MaxFiles = 5000;
    public const string CacheFolderName = ".cache";

    /// <summary>
    /// Every preset and plugin in the folder and its subfolders, sorted by path as File Explorer
    /// sorts names. Hidden files and folders (the conversion cache among them) are skipped, and so
    /// are links, which could lead out of the folder.
    /// </summary>
    public static IReadOnlyList<CustomPresetFile> Scan(string root)
    {
        var files = new List<CustomPresetFile>();
        if (!Directory.Exists(root))
        {
            return files;
        }

        var baseFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var folders = new Stack<string>([baseFolder]);
        while (folders.Count > 0 && files.Count < MaxFiles)
        {
            var folder = folders.Pop();
            IEnumerable<FileSystemInfo> items;
            try
            {
                items = new DirectoryInfo(folder).EnumerateFileSystemInfos().ToList();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var item in items)
            {
                if (IsSkipped(item))
                {
                    continue;
                }

                if (item is DirectoryInfo)
                {
                    folders.Push(item.FullName);
                }
                else if (item is FileInfo file && CustomPresetFile.KindFor(file.Name) is { } kind)
                {
                    var relative = Path.GetRelativePath(baseFolder, file.FullName).Replace('\\', '/');
                    var modified = new DateTimeOffset(file.LastWriteTimeUtc).ToUnixTimeMilliseconds();
                    files.Add(new CustomPresetFile(relative, kind, $"{file.Length}-{modified}"));
                    if (files.Count >= MaxFiles)
                    {
                        break;
                    }
                }
            }
        }

        files.Sort((a, b) => NaturalCompare(a.RelativePath, b.RelativePath));
        return files;
    }

    /// <summary>Compares as File Explorer does: case ignored, and runs of digits by their value ("2" before "10").</summary>
    public static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                var startA = i;
                var startB = j;
                while (i < a.Length && char.IsAsciiDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsAsciiDigit(b[j]))
                {
                    j++;
                }

                var numberA = a[startA..i].TrimStart('0');
                var numberB = b[startB..j].TrimStart('0');
                var order = numberA.Length != numberB.Length
                    ? numberA.Length.CompareTo(numberB.Length)
                    : string.CompareOrdinal(numberA, numberB);
                if (order != 0)
                {
                    return order;
                }

                continue;
            }

            var letters = string.Compare(a[i].ToString(), b[j].ToString(), StringComparison.OrdinalIgnoreCase);
            if (letters != 0)
            {
                return letters;
            }

            i++;
            j++;
        }

        var lengths = (a.Length - i).CompareTo(b.Length - j);
        return lengths != 0 ? lengths : string.CompareOrdinal(a, b);
    }

    private static bool IsSkipped(FileSystemInfo item) =>
        item.Name.StartsWith('.')
        || item.Attributes.HasFlag(FileAttributes.Hidden)
        || item.Attributes.HasFlag(FileAttributes.ReparsePoint);
}

public static partial class PluginMeta
{
    /// <summary>
    /// The display name a plugin declares with <c>export const meta = { name: "…" }</c>, read from
    /// its source without running it. Null if there is none, or it isn't a plain string.
    /// </summary>
    public static string? Name(string source)
    {
        var match = MetaName().Match(source);
        if (!match.Success)
        {
            return null;
        }

        var name = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value).Trim();
        return name.Length == 0 ? null : name;
    }

    [GeneratedRegex("""export\s+const\s+meta\s*=\s*\{[^}]*?\bname\s*:\s*(?:"([^"\\\n]{1,100})"|'([^'\\\n]{1,100})')""", RegexOptions.None, 1000)]
    private static partial Regex MetaName();
}

public static class PresetImport
{
    public static IReadOnlyList<string> Extensions { get; } = [".json", ".js", ".milk"];

    /// <summary>
    /// A name for an imported file or folder that doesn't replace one already in the folder:
    /// "Tunnel.milk", then "Tunnel (2).milk", "Tunnel (3).milk", as File Explorer does.
    /// </summary>
    public static string FreeName(string name, bool isFolder, Func<string, bool> isTaken)
    {
        if (!isTaken(name))
        {
            return name;
        }

        var dot = isFolder ? -1 : name.LastIndexOf('.');
        var stem = dot > 0 ? name[..dot] : name;
        var suffix = dot > 0 ? name[dot..] : string.Empty;
        var number = 2;
        while (isTaken($"{stem} ({number}){suffix}"))
        {
            number++;
        }

        return $"{stem} ({number}){suffix}";
    }

    /// <summary>
    /// Copies files and folders into the presets folder. A name that is taken gets a number;
    /// nothing is ever replaced. Links inside a copied folder are left out, as they could lead anywhere.
    /// </summary>
    /// <returns>How many items were copied, and a line for each one that couldn't be.</returns>
    public static (int Copied, IReadOnlyList<string> Problems) CopyInto(string folder, IEnumerable<string> sources)
    {
        var copied = 0;
        var problems = new List<string>();
        Directory.CreateDirectory(folder);
        foreach (var source in sources)
        {
            var isFolder = Directory.Exists(source);
            var original = Path.GetFileName(Path.TrimEndingDirectorySeparator(source));
            try
            {
                var target = Path.Combine(folder, FreeName(original, isFolder, candidate => Path.Exists(Path.Combine(folder, candidate))));
                if (isFolder)
                {
                    CopyFolder(source, target);
                }
                else
                {
                    File.Copy(source, target, overwrite: false);
                }

                copied++;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{original}: {error.Message}");
            }
        }

        return (copied, problems);
    }

    private static void CopyFolder(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }

        foreach (var folder in Directory.EnumerateDirectories(source))
        {
            if (!new DirectoryInfo(folder).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                CopyFolder(folder, Path.Combine(target, Path.GetFileName(folder)));
            }
        }
    }
}

/// <summary>Converted <c>.milk</c> presets are kept in <c>Presets\.cache\&lt;key&gt;.json</c>, so each file is converted once.</summary>
public static class MilkConversion
{
    /// <summary>Part of every cache key, so results from another converter version aren't reused.</summary>
    public const string ConverterVersion = "milkdrop-preset-converter 0.1.2";
    public const int MaxSourceBytes = 1_000_000;
    public const int MaxResultBytes = 4_000_000;

    /// <summary>The cache key for a <c>.milk</c> file's contents: the same key the Mac computes.</summary>
    public static string CacheKey(byte[] contents)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(ConverterVersion));
        hash.AppendData(contents);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string CachePath(string key) => $"{PresetFolder.CacheFolderName}/{key}.json";

    /// <summary>
    /// A <c>.milk</c> file's text. Many predate UTF-8, so a file that isn't valid UTF-8 is read as
    /// Latin-1, which accepts any byte.
    /// </summary>
    public static string Text(byte[] contents)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(contents).TrimStart((char)0xFEFF);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(contents);
        }
    }

    /// <summary>
    /// Why a file can't be a Milkdrop preset, or null if it looks like one. The converter turns any
    /// text into an empty preset without complaining, so this is checked first.
    /// </summary>
    public static string? Problem(string source)
    {
        if (Encoding.UTF8.GetByteCount(source) > MaxSourceBytes)
        {
            return "File is too large";
        }

        var lowered = source.ToLowerInvariant();
        return !lowered.Contains("[preset", StringComparison.Ordinal) && !lowered.Contains("per_frame_", StringComparison.Ordinal) && !lowered.Contains("fdecay=", StringComparison.Ordinal)
            ? "Not a Milkdrop preset"
            : null;
    }

    /// <summary>
    /// Checks what the converter returned: the preset as JSON text. The page that runs it handles
    /// untrusted files, so its answer is untrusted too.
    /// </summary>
    /// <returns>The JSON to cache and no error, or no JSON and a message saying what is wrong.</returns>
    public static (string? Json, string? Error) CheckResult(string? reply)
    {
        if (reply is null)
        {
            return (null, "The converter returned nothing");
        }

        if (Encoding.UTF8.GetByteCount(reply) > MaxResultBytes)
        {
            return (null, "The converted preset is too large");
        }

        try
        {
            using var document = JsonDocument.Parse(reply, new JsonDocumentOptions { MaxDepth = 64 });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("baseVals", out var values) || values.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("shapes", out var shapes) || shapes.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("waves", out var waves) || waves.ValueKind != JsonValueKind.Array)
            {
                return (null, "The converter returned something that isn't a preset");
            }

            foreach (var shader in new[] { "warp", "comp" })
            {
                // The converter reports a shader it couldn't translate inside the shader text.
                if (root.TryGetProperty(shader, out var code) && code.ValueKind == JsonValueKind.String
                    && code.GetString()!.Contains("parsing failed", StringComparison.Ordinal))
                {
                    return (null, $"The {shader} shader couldn't be converted");
                }
            }

            return (reply, null);
        }
        catch (JsonException)
        {
            return (null, "The converter returned something that isn't a preset");
        }
    }
}

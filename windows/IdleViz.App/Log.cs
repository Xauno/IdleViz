using System.Globalization;

namespace IdleViz.App;

/// <summary>
/// The app's log: one text file under %LOCALAPPDATA%\IdleViz\logs that can be read while the app runs.
/// Every open, close reason and trigger goes here, as on the Mac.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly Lock s_lock = new();

    public static string Folder { get; } = Path.Combine(AppPaths.LocalData, "logs");

    private static string FilePath => Path.Combine(Folder, "idleviz.log");

    public static void Info(string category, string message)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{category}] {message}{Environment.NewLine}");
        lock (s_lock)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                {
                    // One older file is kept, so the log never grows without limit.
                    File.Move(FilePath, Path.Combine(Folder, "idleviz.old.log"), overwrite: true);
                }

                File.AppendAllText(FilePath, line);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                // A log that can't be written must never take the app down.
            }
        }
    }
}

internal static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\IdleViz: settings and logs.</summary>
    public static string LocalData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleViz");

    public static string SettingsFile => Path.Combine(LocalData, "settings.json");
}

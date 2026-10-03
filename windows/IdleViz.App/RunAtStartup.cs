using IdleViz.Core;
using Microsoft.Win32;

namespace IdleViz.App;

/// <summary>
/// "Run at startup": this copy's entry under the user's Run key. The state is read from the
/// registry every time and never stored, because the user can also switch the entry off in
/// Windows' own list of startup apps. That switch is Windows' to change, so it is only read.
/// </summary>
internal static class RunAtStartup
{
    private static string ExecutablePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "IdleViz.exe");

    public static StartupState State
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(StartupEntry.RunKey);
                using var approved = Registry.CurrentUser.OpenSubKey(StartupEntry.ApprovedKey);
                return StartupEntry.State(
                    run?.GetValue(StartupEntry.Name) as string, approved?.GetValue(StartupEntry.Name) as byte[], ExecutablePath);
            }
            catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                Log.Info("startup", $"Couldn't read the startup entry: {error.Message}");
                return StartupState.Off;
            }
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(StartupEntry.RunKey);
            if (enabled)
            {
                run.SetValue(StartupEntry.Name, StartupEntry.Command(ExecutablePath));
            }
            else
            {
                run.DeleteValue(StartupEntry.Name, throwOnMissingValue: false);
            }

            Log.Info("startup", enabled ? $"Runs at startup: {ExecutablePath}" : "Doesn't run at startup");
        }
        catch (Exception error) when (error is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Info("startup", $"Couldn't change the startup entry: {error.Message}");
        }
    }
}

using IdleViz.Core;
using Microsoft.UI.Dispatching;

namespace IdleViz.App;

/// <summary>What the last check from the settings row came to, shown under its switch.</summary>
internal enum UpdateResult
{
    Checking,
    UpToDate,
    Failed,
}

/// <summary>
/// Asks GitHub once a day whether there is a newer release, for the row in the tray flyout and the
/// "Check for updates" card in settings. It only tells: nothing is downloaded or installed.
/// Used from the UI thread only.
/// </summary>
internal sealed class UpdateChecker
{
    private static readonly HttpClient s_http = CreateClient();

    private readonly SettingsStore _settings;
    private readonly DispatcherQueueTimer _timer;
    private readonly bool _pretend;
    private bool _checking;

    /// <param name="pretend">Offer <see cref="UpdateCheck.PretendVersion"/> whatever GitHub says, and only ask it from "Check now" (the debug switch).</param>
    public UpdateChecker(DispatcherQueue dispatcher, SettingsStore settings, bool pretend)
    {
        _settings = settings;
        _pretend = pretend;
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Run(showResult: false);
    }

    /// <summary>Raised when <see cref="Available"/> or <see cref="Result"/> changed.</summary>
    public event Action? Changed;

    public Version? Installed { get; } = UpdateCheck.Installed(typeof(UpdateChecker).Assembly.GetName().Version);

    /// <summary>The release to offer, or null when the running copy is the latest or the check is off.</summary>
    public Version? Available { get; private set; }

    public UpdateResult? Result { get; private set; }

    public bool Enabled => UpdateCheck.IsEnabled(_settings);

    /// <summary>Shows what the last check found and plans the next one. Also follows the switch in settings.</summary>
    public void Start()
    {
        _settings.Changed += (_, e) =>
        {
            if (e.Key == UpdateCheck.EnabledKey)
            {
                Apply();
            }
        };
        Apply();
    }

    /// <summary>The "Check now" button: asks GitHub at once, whenever the last check was.</summary>
    public void CheckNow()
    {
        if (Enabled)
        {
            Run(showResult: true);
        }
    }

    public static async void OpenReleasePage()
    {
        try
        {
            await Windows.System.Launcher.LaunchUriAsync(UpdateCheck.ReleasePage);
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
        {
            Log.Info("updates", $"Couldn't open the release page: {error.Message}");
        }
    }

    private void Apply()
    {
        _timer.Stop();
        Result = null;
        if (Enabled)
        {
            ShowStored();
            if (!_pretend)
            {
                Schedule(UpdateCheck.Wait(UpdateCheck.LastCheck(_settings), DateTimeOffset.UtcNow));
            }
        }
        else
        {
            Available = null;
        }

        Changed?.Invoke();
    }

    private void ShowStored()
    {
        var latest = _pretend ? UpdateCheck.PretendVersion : UpdateCheck.ParseVersion(_settings.GetString(UpdateCheck.LatestKey));
        Available = UpdateCheck.Available(Installed, latest);
    }

    private void Schedule(TimeSpan wait)
    {
        // A check that is due waits a moment too, so it never runs inside the call that planned it.
        _timer.Interval = wait > TimeSpan.FromSeconds(1) ? wait : TimeSpan.FromSeconds(1);
        _timer.Start();
    }

    private async void Run(bool showResult)
    {
        if (_checking)
        {
            return;
        }

        _checking = true;
        _timer.Stop();
        if (showResult)
        {
            Result = UpdateResult.Checking;
            Changed?.Invoke();
        }

        var latest = await FetchLatest();
        _checking = false;
        // Switched off while GitHub was being asked: the row is gone and stays gone.
        if (!Enabled)
        {
            return;
        }

        if (latest is null)
        {
            Log.Info("updates", "Couldn't ask GitHub for the latest release");
        }
        else
        {
            UpdateCheck.Store(_settings, latest, DateTimeOffset.UtcNow);
            Log.Info("updates", $"Latest release is {UpdateCheck.Label(latest)}, this is {(Installed is null ? "unknown" : UpdateCheck.Label(Installed))}");
        }

        ShowStored();
        Result = null;
        if (showResult && latest is null)
        {
            Result = UpdateResult.Failed;
        }
        else if (showResult && Available is null)
        {
            Result = UpdateResult.UpToDate;
        }

        Changed?.Invoke();
        if (!_pretend)
        {
            Schedule(latest is null ? UpdateCheck.RetryInterval : UpdateCheck.Interval);
        }
    }

    private static async Task<Version?> FetchLatest()
    {
        try
        {
            using var response = await s_http.GetAsync(UpdateCheck.LatestRelease);
            return response.IsSuccessStatusCode ? UpdateCheck.LatestVersion(await response.Content.ReadAsStringAsync()) : null;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        // GitHub refuses requests that don't say who is asking.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IdleViz");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}

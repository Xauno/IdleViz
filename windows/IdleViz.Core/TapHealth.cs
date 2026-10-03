namespace IdleViz.Core;

/// <summary>
/// Notices when Spotify says it's playing but the capture hears only exact zeros, or nothing.
/// On Windows that means Spotify plays on another device (Spotify Connect) or is muted in the
/// Windows mixer; a paused Spotify sends zeros too, which is why only playing time counts.
/// Ported from <c>TapHealth.swift</c>, where it is also the sign of a missing permission.
/// </summary>
public sealed class TapHealth
{
    public const int SuspectAfterSeconds = 5;

    private int _silentSeconds;

    /// <summary>True while the capture looks like it hears nothing.</summary>
    public bool Suspected { get; private set; }

    /// <summary>Call once a second with what the capture delivered in that second.</summary>
    /// <returns>True the moment the capture first looks silent, so it can be logged once.</returns>
    public bool Record(int buffers, int zeroBuffers, bool spotifyPlaying)
    {
        if (!spotifyPlaying)
        {
            _silentSeconds = 0;
            return false;
        }

        if (buffers > zeroBuffers)
        {
            _silentSeconds = 0;
            Suspected = false;
            return false;
        }

        _silentSeconds++;
        if (_silentSeconds < SuspectAfterSeconds || Suspected)
        {
            return false;
        }

        Suspected = true;
        return true;
    }
}

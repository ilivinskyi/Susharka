namespace Susharka.Core;

/// <summary>What the pointer is doing this tick, measured by the caller.</summary>
public readonly record struct RevealInput(
    double Now,
    /// <summary>Pointer rests in the thin strip along the top edge of a monitor.</summary>
    bool InRevealBand,
    /// <summary>Pointer is within the area covered by the line (top edge down to below the photos).</summary>
    bool InLineZone,
    /// <summary>Something keeps the line open: pinned, a drag, a press, a menu, a fresh capture peeking.</summary>
    bool HeldOpen,
    /// <summary>A full-screen app owns the monitor, or a mouse button is down (dragging a window, selecting text).</summary>
    bool Blocked);

/// <summary>
/// Dwell-to-reveal / linger-to-retract state machine. Pure, so it can be tested with a fake clock.
/// Reveal after resting in the top band for <see cref="RevealDelay"/>; retract once the pointer has been
/// outside the line's zone for <see cref="RetractDelay"/>.
/// </summary>
public sealed class RevealLogic
{
    public double RevealDelay { get; set; } = 0.4;
    public double RetractDelay { get; set; } = 0.5;

    public bool Shown { get; private set; }

    private double? _dwellStart;
    private double? _leftAt;

    /// <returns>True when <see cref="Shown"/> changed.</returns>
    public bool Update(RevealInput i)
    {
        if (!Shown)
        {
            if (i.HeldOpen) return Set(true, i.Now);
            if (i.InRevealBand && !i.Blocked)
            {
                _dwellStart ??= i.Now;
                if (i.Now - _dwellStart.Value >= RevealDelay) return Set(true, i.Now);
            }
            else _dwellStart = null;
            return false;
        }

        if (i.HeldOpen || i.InLineZone)
        {
            _leftAt = null;
            return false;
        }
        _leftAt ??= i.Now;
        if (i.Now - _leftAt.Value >= RetractDelay) return Set(false, i.Now);
        return false;
    }

    /// <summary>Forces a state (hotkey, tray click).</summary>
    public bool Set(bool shown, double now)
    {
        _dwellStart = null;
        _leftAt = null;
        if (Shown == shown) return false;
        Shown = shown;
        return true;
    }
}

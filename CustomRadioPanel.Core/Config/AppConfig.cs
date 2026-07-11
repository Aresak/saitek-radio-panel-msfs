namespace CustomRadioPanel.Core.Config;

/// <summary>User-tunable behaviour. Persisted as JSON; every value has a sensible built-in default.</summary>
public sealed class AppConfig
{
    public EncoderConfig Encoder { get; set; } = new();
    public DisplayConfig Display { get; set; } = new();
    public WelcomeConfig Welcome { get; set; } = new();

    /// <summary>How long the ACT/STBY toggle must be held to count as a long press (ms).</summary>
    public int LongPressMs { get; set; } = 500;
}

/// <summary>Scrolling welcome message shown on the panel at startup.</summary>
public sealed class WelcomeConfig
{
    /// <summary>Show a scrolling welcome message on the panel at startup.</summary>
    public bool Enabled { get; set; } = true;
    public string UpperMessage { get; set; } = "Enjoy your flight!";
    public string LowerMessage { get; set; } = "buymeacoffee.com/aresak";
    /// <summary>Scroll speed = character shifts per second.</summary>
    public int Speed { get; set; } = 5;
    /// <summary>How long (seconds) the welcome plays before normal operation resumes.</summary>
    public int Time { get; set; } = 12;
}

/// <summary>
/// Controls knob feel — the user's main complaint about the old SPAD.neXt profile.
/// Base steps are per radio type; acceleration multiplies the step when detents arrive quickly,
/// so a fast spin covers a big range without spamming single steps.
/// </summary>
public sealed class EncoderConfig
{
    public bool AccelerationEnabled { get; set; } = true;

    /// <summary>Detents faster than this (ms apart) use <see cref="FastMultiplier"/>.</summary>
    public int FastThresholdMs { get; set; } = 45;

    /// <summary>Detents faster than this (ms apart) use <see cref="MediumMultiplier"/>.</summary>
    public int MediumThresholdMs { get; set; } = 110;

    public int MediumMultiplier { get; set; } = 3;
    public int FastMultiplier { get; set; } = 10;

    /// <summary>Global scale applied to every step (1.0 = default feel). Lets the user dull or sharpen all knobs at once.</summary>
    public double GlobalStepScale { get; set; } = 1.0;
}

/// <summary>Display formatting choices.</summary>
public sealed class DisplayConfig
{
    /// <summary>Use 8.33 kHz COM channel spacing for the inner-knob step instead of 25 kHz.</summary>
    public bool Com833Spacing { get; set; } = true;

    /// <summary>
    /// Drop the always-1 hundreds digit and show 3 decimals on COM/NAV (123.405 → 23.405).
    /// Recommended for VATSIM / 8.33 kHz. When false, shows classic 118.30 style.
    /// </summary>
    public bool HideHundredsDigit { get; set; } = true;

    /// <summary>When true, the standby side is blanked if it equals the active frequency.</summary>
    public bool BlankStandbyWhenEqual { get; set; } = false;
}

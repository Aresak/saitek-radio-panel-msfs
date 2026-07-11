namespace CustomRadioPanel.Core.Sim;

/// <summary>
/// Latest radio-related values read from the simulator. Frequencies are kept in the natural unit
/// used by the corresponding SimConnect variable (COM/NAV in MHz, ADF in kHz).
/// </summary>
public sealed class RadioSnapshot
{
    public double Com1Active { get; set; }
    public double Com1Standby { get; set; }
    public double Com2Active { get; set; }
    public double Com2Standby { get; set; }

    public double Nav1Active { get; set; }
    public double Nav1Standby { get; set; }
    public double Nav2Active { get; set; }
    public double Nav2Standby { get; set; }

    public double Adf1 { get; set; }
    public double Adf2 { get; set; }

    public double Dme1 { get; set; }
    public double Dme2 { get; set; }

    /// <summary>Transponder squawk as a 4-digit BCD number (e.g. 0x1200 style read as a decimal 4-digit).</summary>
    public double TransponderCode { get; set; }

    public double KohlsmanMb { get; set; }

    public RadioSnapshot Clone() => (RadioSnapshot)MemberwiseClone();
}

namespace CustomRadioPanel.Core.Hardware;

/// <summary>The two independent halves of the radio panel (each with its own selector, displays and knobs).</summary>
public enum PanelRow
{
    Top,
    Bottom,
}

/// <summary>Position of a row's rotary mode selector. Values match the bit order in the HID input report.</summary>
public enum RadioMode
{
    Com1 = 0,
    Com2 = 1,
    Nav1 = 2,
    Nav2 = 3,
    Adf = 4,
    Dme = 5,
    Xpdr = 6,
}

/// <summary>Which of the two concentric tuning knobs on a row was turned.</summary>
public enum Knob
{
    /// <summary>Small inner knob — fine step (e.g. kHz / small digits).</summary>
    Inner,

    /// <summary>Large outer knob — coarse step (e.g. MHz / large digits).</summary>
    Outer,
}

/// <summary>Rotation direction of a knob.</summary>
public enum TurnDirection
{
    Clockwise = 1,
    CounterClockwise = -1,
}

/// <summary>Base type for a decoded panel input event.</summary>
public abstract record PanelEvent(PanelRow Row);

/// <summary>The row's mode selector moved to a new position.</summary>
public sealed record SelectorChanged(PanelRow Row, RadioMode Mode) : PanelEvent(Row);

/// <summary>A knob was turned by one or more detents in a single report.</summary>
public sealed record EncoderTurned(PanelRow Row, Knob Knob, TurnDirection Direction, int Ticks) : PanelEvent(Row);

/// <summary>The ACT/STBY toggle for the row was pressed down (rising edge).</summary>
public sealed record ActStbyDown(PanelRow Row) : PanelEvent(Row);

/// <summary>The ACT/STBY toggle for the row was released (falling edge).</summary>
public sealed record ActStbyUp(PanelRow Row) : PanelEvent(Row);

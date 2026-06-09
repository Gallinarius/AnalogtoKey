using System.Text.Json.Serialization;

namespace AnalogtoKey.Models;

public class AxisStepMapping
{
    public string Label    { get; set; } = "Axis";
    public string AxisName { get; set; } = "AxisX";

    public bool UseStandard { get; set; } = true;
    public bool UseCenter   { get; set; } = false;
    public bool UseCp       { get; set; } = false;

    // Standard mode — pulsed keys
    public ushort UpKey   { get; set; } = 0;
    public ushort DownKey { get; set; } = 0;

    // Steps: without Center = StepsUp only; with Center = StepsUp (throttle) + StepsDown (brake)
    // JsonPropertyName keeps backwards-compat with old profiles that wrote "Steps"
    [JsonPropertyName("Steps")]
    public int StepsUp   { get; set; } = 5;
    public int StepsDown { get; set; } = 5;

    // Constant Pressure mode — held keys
    public ushort CpUpKey   { get; set; } = 0;
    public ushort CpDownKey { get; set; } = 0;

    // Center Key — pulsed once when axis enters the neutral zone (from either direction)
    public ushort CenterKey { get; set; } = 0;

    // Dead zone % around center axis (1–49) — used by Center and CP modes
    public int DeadZonePercent { get; set; } = 5;

    // End-of-travel keypresses — pulsed once when axis first reaches the extreme step
    public ushort MaxKey { get; set; } = 0;
    public ushort MinKey { get; set; } = 0;

    public int CalMin { get; set; } = 0;
    public int CalMax { get; set; } = 65535;

    // Stacked mode — queue keypresses sequentially instead of fire-and-forget (prevents step desync at high speed)
    public bool StackedMode { get; set; } = false;
    public int  KeyHoldMs   { get; set; } = 50; // hold duration per pulsed keypress (ms)
    public int  KeyPauseMs  { get; set; } = 0;  // pause between queued presses; 0 = same as KeyHoldMs

    // UI state: whether the Advanced section is expanded in the editor
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ShowAdvanced { get; set; } = false;

    public bool NeedsAdvanced =>
        !UseStandard || UseCenter || UseCp || MaxKey != 0 || MinKey != 0 || StackedMode;
}

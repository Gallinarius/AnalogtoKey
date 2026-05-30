namespace AnalogtoKey.Models;

public class AxisStepMapping
{
    public string Label    { get; set; } = "Axis";
    public string AxisName { get; set; } = "AxisX";
    public ushort UpKey   { get; set; } = 0;
    public ushort DownKey { get; set; } = 0;
    public int    Steps   { get; set; } = 5;
    public int    CalMin  { get; set; } = 0;
    public int    CalMax  { get; set; } = 65535;
}

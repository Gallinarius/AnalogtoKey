using System.Collections.Generic;

namespace AnalogtoKey.Models
{
    public record DeviceCapabilities(int ButtonCount, IReadOnlyList<string> AxisNames);
}

using System.Collections.Generic;

namespace AnalogtoKey.Models
{
    public class MappingProfile
    {
        public string Name { get; set; } = "Default";
        public int KeyHoldMs { get; set; } = 50;
        public Dictionary<string, StickMapping> Controllers { get; set; } = new();

        public StickMapping GetOrCreate(string guid)
        {
            if (!Controllers.TryGetValue(guid, out var m))
                Controllers[guid] = m = new StickMapping();
            return m;
        }
    }

    public class StickMapping
    {
        public List<AxisStepMapping> AxisMappings { get; set; } = new()
        {
            new() { Label = "Axis 1" },
            new() { Label = "Axis 2" },
        };

        public Dictionary<int, ushort> HatMappings { get; set; } = new()
        {
            { 0,     0 },
            { 4500,  0 },
            { 9000,  0 },
            { 13500, 0 },
            { 18000, 0 },
            { 22500, 0 },
            { 27000, 0 },
            { 31500, 0 },
        };

        public Dictionary<int, ushort> ButtonMappings { get; set; } = new();
    }
}

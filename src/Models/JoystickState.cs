using System.Collections.Generic;

namespace AnalogtoKey.Models
{
    public class StickState
    {
        public string DeviceGuid { get; set; } = "";
        public string Name { get; set; } = "";

        public int AxisX { get; set; }
        public int AxisY { get; set; }
        public int AxisZ { get; set; }
        public int AxisRx { get; set; }
        public int AxisRy { get; set; }
        public int AxisRz { get; set; }

        public int HatSwitch { get; set; }

        public bool[] Buttons { get; set; } = new bool[32];

        public bool IsConnected { get; set; }

        public int GetAxis(string name) => name switch
        {
            "AxisX"  => AxisX,
            "AxisY"  => AxisY,
            "AxisZ"  => AxisZ,
            "AxisRx" => AxisRx,
            "AxisRy" => AxisRy,
            "AxisRz" => AxisRz,
            _        => 0
        };
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDX.DirectInput;
using AnalogtoKey.Models;
using System.Runtime.InteropServices;

namespace AnalogtoKey.Services
{
    public class InputService : IDisposable
    {
        // Standard DirectInput axis type GUIDs — stable across all Windows versions
        private static readonly Guid GuidXAxis  = new("A36D02E0-C9F3-11CF-BFC7-444553540000");
        private static readonly Guid GuidYAxis  = new("A36D02E1-C9F3-11CF-BFC7-444553540000");
        private static readonly Guid GuidZAxis  = new("A36D02E2-C9F3-11CF-BFC7-444553540000");
        private static readonly Guid GuidRxAxis = new("A36D02F4-C9F3-11CF-BFC7-444553540000");
        private static readonly Guid GuidRyAxis = new("A36D02F5-C9F3-11CF-BFC7-444553540000");
        private static readonly Guid GuidRzAxis = new("A36D02E3-C9F3-11CF-BFC7-444553540000");
        private static readonly Guid GuidSlider = new("A36D02E4-C9F3-11CF-BFC7-444553540000");

        private readonly DirectInput _directInput = new DirectInput();
        private readonly List<Joystick> _joysticks = new();
        private readonly List<StickState> _states = new();
        private readonly List<string>              _connectedVids    = new();
        private readonly List<(string Guid, string Name)> _connectedDevices = new();
        private readonly Dictionary<string, DeviceCapabilities> _capabilities = new();

        private CancellationTokenSource? _cts;
        private Task? _pollTask;

        public event Action<List<StickState>>? StateUpdated;

        public IReadOnlyList<string>                    ConnectedVids    => _connectedVids;
        public IReadOnlyList<(string Guid, string Name)> ConnectedDevices => _connectedDevices;

        public DeviceCapabilities GetCapabilities(string guid) =>
            _capabilities.TryGetValue(guid, out var caps) ? caps :
            new DeviceCapabilities(16, new[] { "AxisX", "AxisY", "AxisZ", "AxisRx", "AxisRy", "AxisRz" });

        public void Start()
        {
            InitializeDevices();

            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoop(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            _pollTask?.Wait(500);
            DisposeJoysticks();
        }

        public void Rescan()
        {
            _cts?.Cancel();
            _pollTask?.Wait(500);
            InitializeDevices();
            _cts = new CancellationTokenSource();
            _pollTask = Task.Run(() => PollLoop(_cts.Token));
        }

        private void InitializeDevices()
        {
            DisposeJoysticks();
            _joysticks.Clear();
            _states.Clear();
            _connectedVids.Clear();
            _connectedDevices.Clear();
            _capabilities.Clear();

            // DeviceClass.GameControl covers all game input devices: joysticks, gamepads,
            // flight sim controllers (e.g. Honeycomb Bravo, X-55 Throttle), racing wheels, etc.
            var devices = _directInput
                .GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices)
                .ToList();

            foreach (var device in devices)
            {
                try
                {
                    var joystick = new Joystick(_directInput, device.InstanceGuid);
                    joystick.Properties.BufferSize = 128;
                    joystick.Acquire();

                    var guid = device.InstanceGuid.ToString();
                    _joysticks.Add(joystick);
                    _states.Add(new StickState
                    {
                        DeviceGuid  = guid,
                        Name        = device.InstanceName,
                        IsConnected = true
                    });
                    _connectedDevices.Add((guid, device.InstanceName));
                    _capabilities[guid] = QueryCapabilities(joystick);

                    var b   = device.ProductGuid.ToByteArray();
                    var vid = $"VID_{((b[3] << 8) | b[2]):X4}";
                    if (!_connectedVids.Contains(vid))
                        _connectedVids.Add(vid);
                }
                catch
                {
                    // Device cannot be acquired — skip it
                }
            }
        }

        private DeviceCapabilities QueryCapabilities(Joystick joystick)
        {
            int buttonCount = Math.Clamp(joystick.Capabilities.ButtonCount, 1, 32);

            var axes       = new List<string>();
            int sliderIdx  = 0;
            try
            {
                var objects = joystick.GetObjects(DeviceObjectTypeFlags.Axis);
                foreach (var obj in objects)
                {
                    var g = obj.ObjectType;
                    string? name =
                        g == GuidXAxis  ? "AxisX"  :
                        g == GuidYAxis  ? "AxisY"  :
                        g == GuidZAxis  ? "AxisZ"  :
                        g == GuidRxAxis ? "AxisRx" :
                        g == GuidRyAxis ? "AxisRy" :
                        g == GuidRzAxis ? "AxisRz" :
                        g == GuidSlider && sliderIdx < 2 ? $"Slider{sliderIdx}" : null;

                    if (name == null) continue;
                    if (!axes.Contains(name))
                    {
                        axes.Add(name);
                        if (name.StartsWith("Slider")) sliderIdx++;
                    }
                }
            }
            catch { }

            if (axes.Count == 0)
                axes.AddRange(new[] { "AxisX", "AxisY", "AxisZ" });

            // Sort into canonical order regardless of what the device reported
            var order = new[] { "AxisX", "AxisY", "AxisZ", "AxisRx", "AxisRy", "AxisRz", "Slider0", "Slider1" };
            axes.Sort((a, b) => Array.IndexOf(order, a).CompareTo(Array.IndexOf(order, b)));

            return new DeviceCapabilities(buttonCount, axes);
        }

        private void PollLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                for (int i = 0; i < _joysticks.Count; i++)
                {
                    try
                    {
                        _joysticks[i].Poll();
                        var raw = _joysticks[i].GetCurrentState();
                        var state = _states[i];

                        state.AxisX  = raw.X;
                        state.AxisY  = raw.Y;
                        state.AxisZ  = raw.Z;
                        state.AxisRx = raw.RotationX;
                        state.AxisRy = raw.RotationY;
                        state.AxisRz = raw.RotationZ;
                        if (raw.Sliders.Length > 0) state.Slider0 = raw.Sliders[0];
                        if (raw.Sliders.Length > 1) state.Slider1 = raw.Sliders[1];
                        state.HatSwitch = raw.PointOfViewControllers.Length > 0
                            ? raw.PointOfViewControllers[0]
                            : -1;

                        var buttons = raw.Buttons;
                        for (int b = 0; b < Math.Min(buttons.Length, state.Buttons.Length); b++)
                            state.Buttons[b] = buttons[b];

                        state.IsConnected = true;
                    }
                    catch
                    {
                        _states[i].IsConnected = false;
                    }
                }

                StateUpdated?.Invoke(_states);
                Thread.Sleep(16); // ~60 Hz polling
            }
        }

        private void DisposeJoysticks()
        {
            foreach (var j in _joysticks)
                try { j.Unacquire(); j.Dispose(); } catch { }
        }

        public void Dispose()
        {
            Stop();
            _directInput.Dispose();
        }
    }
}

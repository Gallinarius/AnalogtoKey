using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpDX.DirectInput;
using AnalogtoKey.Models;

namespace AnalogtoKey.Services
{
    public class InputService : IDisposable
    {
        private readonly DirectInput _directInput = new DirectInput();
        private readonly List<Joystick> _joysticks = new();
        private readonly List<StickState> _states = new();
        private readonly List<string>              _connectedVids    = new();
        private readonly List<(string Guid, string Name)> _connectedDevices = new();

        private CancellationTokenSource? _cts;
        private Task? _pollTask;

        public event Action<List<StickState>>? StateUpdated;

        public IReadOnlyList<string>                    ConnectedVids    => _connectedVids;
        public IReadOnlyList<(string Guid, string Name)> ConnectedDevices => _connectedDevices;

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

        private void InitializeDevices()
        {
            DisposeJoysticks();
            _joysticks.Clear();
            _states.Clear();
            _connectedVids.Clear();
            _connectedDevices.Clear();

            var devices = _directInput
                .GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AllDevices)
                .Concat(_directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices))
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

                        state.AxisX = raw.X;
                        state.AxisY = raw.Y;
                        state.AxisZ = raw.Z;
                        state.AxisRx = raw.RotationX;
                        state.AxisRy = raw.RotationY;
                        state.AxisRz = raw.RotationZ;
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

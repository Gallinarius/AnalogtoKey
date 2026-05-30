using System;
using System.Collections.Generic;
using AnalogtoKey.Models;

namespace AnalogtoKey.Services
{
    public class InputMapper
    {
        private MappingProfile _profile;

        private readonly Dictionary<string, HashSet<ushort>> _held      = new();
        private readonly Dictionary<string, int[]>           _prevSteps = new();

        // Fyres kun for akse-trin (knapper/hat/trigger håndteres af MainWindow via currentPressed)
        public event Action<string, string, ushort>? AxisStepSent; // (deviceGuid, label, vk)

        public InputMapper(MappingProfile profile) => _profile = profile;

        public void UpdateProfile(MappingProfile profile)
        {
            ReleaseAll();
            _prevSteps.Clear();
            _profile = profile;
        }

        public void ProcessStates(List<StickState> states)
        {
            foreach (var state in states)
            {
                if (!_held.TryGetValue(state.DeviceGuid, out var held))
                    _held[state.DeviceGuid] = held = new();

                if (!_profile.Controllers.TryGetValue(state.DeviceGuid, out var mapping))
                {
                    ReleaseHeld(held);
                    continue;
                }

                var steps = GetOrCreatePrevSteps(state.DeviceGuid, mapping.AxisMappings.Count);
                ProcessStick(state, mapping, held, steps, AxisStepSent);
            }
        }

        private int[] GetOrCreatePrevSteps(string guid, int count)
        {
            if (!_prevSteps.TryGetValue(guid, out var steps) || steps.Length != count)
            {
                steps = new int[count];
                Array.Fill(steps, -1);
                _prevSteps[guid] = steps;
            }
            return steps;
        }

        public int[] GetPrevSteps(string guid) => GetOrCreatePrevSteps(guid, 2);

        private static void ProcessStick(StickState state, StickMapping mapping,
            HashSet<ushort> held, int[] prevSteps,
            Action<string, string, ushort>? axisStepSent)
        {
            if (!state.IsConnected)
            {
                ReleaseHeld(held);
                return;
            }

            var desired = new HashSet<ushort>();

            if (state.HatSwitch != -1)
            {
                int sector     = (int)Math.Round(state.HatSwitch / 4500.0) % 8;
                int normalized = sector * 4500;
                if (mapping.HatMappings.TryGetValue(normalized, out var hatKey) && hatKey != VKey.None)
                    desired.Add(hatKey);
            }

            for (int i = 0; i < state.Buttons.Length; i++)
                if (state.Buttons[i] && mapping.ButtonMappings.TryGetValue(i, out var btnKey) && btnKey != VKey.None)
                    desired.Add(btnKey);

            foreach (var key in desired)
                if (!held.Contains(key))
                    KeySender.KeyDown(key);
            foreach (var key in new HashSet<ushort>(held))
                if (!desired.Contains(key))
                    KeySender.KeyUp(key);
            held.Clear();
            foreach (var key in desired)
                held.Add(key);

            // Stepped axis mappings
            for (int i = 0; i < Math.Min(mapping.AxisMappings.Count, prevSteps.Length); i++)
            {
                var axMap = mapping.AxisMappings[i];
                int range = axMap.CalMax - axMap.CalMin;
                if (range <= 0) continue;

                int raw     = Math.Clamp(state.GetAxis(axMap.AxisName), axMap.CalMin, axMap.CalMax);
                int curStep = (int)Math.Round((double)(raw - axMap.CalMin) / range * axMap.Steps);

                if (prevSteps[i] < 0) { prevSteps[i] = curStep; continue; }

                int diff = curStep - prevSteps[i];
                prevSteps[i] = curStep;

                if (diff > 0 && axMap.UpKey != VKey.None)
                    for (int n = 0; n < diff; n++)
                    {
                        KeySender.KeyDown(axMap.UpKey);
                        KeySender.KeyUp(axMap.UpKey);
                        axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▲", axMap.UpKey);
                    }
                else if (diff < 0 && axMap.DownKey != VKey.None)
                    for (int n = 0; n < -diff; n++)
                    {
                        KeySender.KeyDown(axMap.DownKey);
                        KeySender.KeyUp(axMap.DownKey);
                        axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▼", axMap.DownKey);
                    }
            }
        }

        private static void ReleaseHeld(HashSet<ushort> held)
        {
            foreach (var key in held) KeySender.KeyUp(key);
            held.Clear();
        }

        public void ReleaseAll()
        {
            foreach (var held in _held.Values) ReleaseHeld(held);
        }
    }
}

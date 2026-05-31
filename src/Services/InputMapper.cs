using System;
using System.Collections.Generic;
using AnalogtoKey.Models;

namespace AnalogtoKey.Services
{
    public class InputMapper
    {
        private MappingProfile _profile;

        private readonly Dictionary<string, HashSet<ushort>>          _held      = new();
        private readonly Dictionary<string, int[]>                     _prevSteps = new();
        private readonly Dictionary<string, (bool up, bool down)[]>   _cpHeld    = new();

        public event Action<string, string, ushort>? AxisStepSent;

        public InputMapper(MappingProfile profile) => _profile = profile;

        public void UpdateProfile(MappingProfile profile)
        {
            ReleaseCpAll();
            ReleaseAll();
            _prevSteps.Clear();
            _cpHeld.Clear();
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

                int axCount = mapping.AxisMappings.Count;
                var steps   = GetOrCreatePrevSteps(state.DeviceGuid, axCount);
                var cpHeld  = GetOrCreateCpHeld(state.DeviceGuid, axCount);
                ProcessStick(state, mapping, held, steps, cpHeld, AxisStepSent);
            }
        }

        private int[] GetOrCreatePrevSteps(string guid, int count)
        {
            if (!_prevSteps.TryGetValue(guid, out var steps) || steps.Length != count)
            {
                steps = new int[count];
                Array.Fill(steps, int.MinValue);
                _prevSteps[guid] = steps;
            }
            return steps;
        }

        private (bool up, bool down)[] GetOrCreateCpHeld(string guid, int count)
        {
            if (!_cpHeld.TryGetValue(guid, out var held) || held.Length != count)
            {
                held = new (bool, bool)[count];
                _cpHeld[guid] = held;
            }
            return held;
        }

        // Called when axis name changes in UI — resets step tracking and releases held CP keys
        public void ResetAxisState(string guid)
        {
            if (_prevSteps.TryGetValue(guid, out var steps))
                Array.Fill(steps, int.MinValue);

            if (_cpHeld.TryGetValue(guid, out var cpHeld) &&
                _profile.Controllers.TryGetValue(guid, out var mapping))
            {
                for (int i = 0; i < Math.Min(cpHeld.Length, mapping.AxisMappings.Count); i++)
                {
                    var ax = mapping.AxisMappings[i];
                    if (cpHeld[i].up   && ax.CpUpKey   != 0) KeySender.KeyUp(ax.CpUpKey);
                    if (cpHeld[i].down && ax.CpDownKey != 0) KeySender.KeyUp(ax.CpDownKey);
                    cpHeld[i] = (false, false);
                }
            }
        }

        private static void ProcessStick(
            StickState state, StickMapping mapping,
            HashSet<ushort> held, int[] prevSteps, (bool up, bool down)[] cpHeld,
            Action<string, string, ushort>? axisStepSent)
        {
            if (!state.IsConnected)
            {
                ReleaseHeld(held);
                for (int i = 0; i < Math.Min(cpHeld.Length, mapping.AxisMappings.Count); i++)
                {
                    var ax = mapping.AxisMappings[i];
                    if (cpHeld[i].up   && ax.CpUpKey   != 0) KeySender.KeyUp(ax.CpUpKey);
                    if (cpHeld[i].down && ax.CpDownKey != 0) KeySender.KeyUp(ax.CpDownKey);
                    cpHeld[i] = (false, false);
                }
                return;
            }

            // ── Buttons + hat ────────────────────────────────────────────────
            var desired = new HashSet<ushort>();

            if (state.HatSwitch != -1)
            {
                int sector     = (int)Math.Round(state.HatSwitch / 4500.0) % 8;
                int normalized = sector * 4500;
                if (mapping.HatMappings.TryGetValue(normalized, out var hatKey) && hatKey != 0)
                    desired.Add(hatKey);
            }

            for (int i = 0; i < state.Buttons.Length; i++)
                if (state.Buttons[i] && mapping.ButtonMappings.TryGetValue(i, out var btnKey) && btnKey != 0)
                    desired.Add(btnKey);

            foreach (var key in desired)
                if (!held.Contains(key)) KeySender.KeyDown(key);
            foreach (var key in new HashSet<ushort>(held))
                if (!desired.Contains(key)) KeySender.KeyUp(key);
            held.Clear();
            foreach (var key in desired) held.Add(key);

            // ── Axis mappings ────────────────────────────────────────────────
            int axCount = Math.Min(mapping.AxisMappings.Count, Math.Min(prevSteps.Length, cpHeld.Length));
            for (int i = 0; i < axCount; i++)
            {
                var axMap = mapping.AxisMappings[i];
                int range = axMap.CalMax - axMap.CalMin;
                if (range <= 0) continue;

                int raw     = Math.Clamp(state.GetAxis(axMap.AxisName), axMap.CalMin, axMap.CalMax);
                int center  = (axMap.CalMin + axMap.CalMax) / 2;
                int deadAbs = Math.Max(1, (int)(range * axMap.DeadZonePercent / 100.0 / 2));

                // ── Standard mode (pulsed steps) ──────────────────────────────
                if (axMap.UseStandard)
                {
                    int curStep;
                    if (axMap.UseCenter)
                    {
                        if (raw >= center - deadAbs && raw <= center + deadAbs)
                        {
                            curStep = 0;
                        }
                        else if (raw > center + deadAbs)
                        {
                            double tRange = axMap.CalMax - (center + deadAbs);
                            curStep = tRange > 0
                                ? (int)Math.Round((raw - center - deadAbs) / tRange * axMap.StepsUp)
                                : axMap.StepsUp;
                            curStep = Math.Clamp(curStep, 1, axMap.StepsUp);
                        }
                        else
                        {
                            double bRange = (center - deadAbs) - axMap.CalMin;
                            curStep = bRange > 0
                                ? -(int)Math.Round((center - deadAbs - raw) / bRange * axMap.StepsDown)
                                : -axMap.StepsDown;
                            curStep = Math.Clamp(curStep, -axMap.StepsDown, -1);
                        }
                    }
                    else
                    {
                        curStep = (int)Math.Round((double)(raw - axMap.CalMin) / range * axMap.StepsUp);
                        curStep = Math.Clamp(curStep, 0, axMap.StepsUp);
                    }

                    if (prevSteps[i] == int.MinValue)
                    {
                        prevSteps[i] = curStep;
                    }
                    else
                    {
                        int diff = curStep - prevSteps[i];
                        prevSteps[i] = curStep;

                        if (diff > 0 && axMap.UpKey != 0)
                            for (int n = 0; n < diff; n++)
                            {
                                KeySender.KeyDown(axMap.UpKey);
                                KeySender.KeyUp(axMap.UpKey);
                                axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▲", axMap.UpKey);
                            }
                        else if (diff < 0 && axMap.DownKey != 0)
                            for (int n = 0; n < -diff; n++)
                            {
                                KeySender.KeyDown(axMap.DownKey);
                                KeySender.KeyUp(axMap.DownKey);
                                axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▼", axMap.DownKey);
                            }
                    }
                }

                // ── Constant Pressure mode (held keys) ────────────────────────
                if (axMap.UseCp)
                {
                    bool wantsUp   = raw > center + deadAbs;
                    bool wantsDown = raw < center - deadAbs;

                    if (wantsUp && !cpHeld[i].up && axMap.CpUpKey != 0)
                    {
                        KeySender.KeyDown(axMap.CpUpKey);
                        cpHeld[i] = (true, cpHeld[i].down);
                        axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▲ HOLD", axMap.CpUpKey);
                    }
                    else if (!wantsUp && cpHeld[i].up)
                    {
                        if (axMap.CpUpKey != 0) KeySender.KeyUp(axMap.CpUpKey);
                        cpHeld[i] = (false, cpHeld[i].down);
                    }

                    if (wantsDown && !cpHeld[i].down && axMap.CpDownKey != 0)
                    {
                        KeySender.KeyDown(axMap.CpDownKey);
                        cpHeld[i] = (cpHeld[i].up, true);
                        axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▼ HOLD", axMap.CpDownKey);
                    }
                    else if (!wantsDown && cpHeld[i].down)
                    {
                        if (axMap.CpDownKey != 0) KeySender.KeyUp(axMap.CpDownKey);
                        cpHeld[i] = (cpHeld[i].up, false);
                    }
                }
            }
        }

        private void ReleaseCpAll()
        {
            foreach (var (guid, cpHeld) in _cpHeld)
            {
                if (!_profile.Controllers.TryGetValue(guid, out var mapping)) continue;
                for (int i = 0; i < Math.Min(cpHeld.Length, mapping.AxisMappings.Count); i++)
                {
                    var ax = mapping.AxisMappings[i];
                    if (cpHeld[i].up   && ax.CpUpKey   != 0) KeySender.KeyUp(ax.CpUpKey);
                    if (cpHeld[i].down && ax.CpDownKey != 0) KeySender.KeyUp(ax.CpDownKey);
                    cpHeld[i] = (false, false);
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
            ReleaseCpAll();
        }
    }
}

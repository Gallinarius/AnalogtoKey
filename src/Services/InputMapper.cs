using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AnalogtoKey.Models;

namespace AnalogtoKey.Services
{
    public class InputMapper : IDisposable
    {
        private MappingProfile _profile;

        private readonly Dictionary<string, HashSet<ushort>>                          _held      = new();
        private readonly Dictionary<string, int[]>                                     _prevSteps = new();
        private readonly Dictionary<string, (bool up, bool down)[]>                   _cpHeld    = new();
        private readonly Dictionary<string, StepQueue?[]>                              _queues    = new();
        private readonly Dictionary<string, Dictionary<int, (ushort key, ushort mod)>> _heldBtns  = new();

        public event Action<string, string, ushort>? AxisStepSent;
        public event Action<ushort>?                 KeyMuted;

        public volatile bool IsTransmitting = true;

        public InputMapper(MappingProfile profile) => _profile = profile;

        public void SetTransmitting(bool value)
        {
            if (value == IsTransmitting) return;
            if (!value)
            {
                ReleaseAll();
                foreach (var arr in _queues.Values)
                    foreach (var q in arr) q?.Clear();
            }
            else
            {
                foreach (var h in _held.Values) h.Clear();
                _cpHeld.Clear();
                foreach (var b in _heldBtns.Values) b.Clear();
            }
            IsTransmitting = value;
        }

        public void UpdateProfile(MappingProfile profile)
        {
            ReleaseCpAll();
            ReleaseAll();
            _prevSteps.Clear();
            _cpHeld.Clear();
            DisposeAllQueues();
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

                int axCount  = mapping.AxisMappings.Count;
                var steps    = GetOrCreatePrevSteps(state.DeviceGuid, axCount);
                var cpHeld   = GetOrCreateCpHeld(state.DeviceGuid, axCount);
                var queues   = GetOrCreateQueues(state.DeviceGuid, mapping.AxisMappings);
                var heldBtns = GetOrCreateHeldBtns(state.DeviceGuid);
                ProcessStick(state, mapping, held, steps, cpHeld, AxisStepSent, IsTransmitting, KeyMuted, queues, heldBtns);
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

        private Dictionary<int, (ushort key, ushort mod)> GetOrCreateHeldBtns(string guid)
        {
            if (!_heldBtns.TryGetValue(guid, out var d))
                _heldBtns[guid] = d = new();
            return d;
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
            Action<string, string, ushort>? axisStepSent,
            bool transmitting, Action<ushort>? keyMuted,
            StepQueue?[]? queues,
            Dictionary<int, (ushort key, ushort mod)> heldBtns)
        {
            if (!state.IsConnected)
            {
                if (transmitting)
                {
                    ReleaseHeld(held);
                    for (int i = 0; i < Math.Min(cpHeld.Length, mapping.AxisMappings.Count); i++)
                    {
                        var ax = mapping.AxisMappings[i];
                        if (cpHeld[i].up   && ax.CpUpKey   != 0) KeySender.KeyUp(ax.CpUpKey);
                        if (cpHeld[i].down && ax.CpDownKey != 0) KeySender.KeyUp(ax.CpDownKey);
                        cpHeld[i] = (false, false);
                    }
                    ReleaseBtns(heldBtns);
                }
                else
                {
                    held.Clear();
                    heldBtns.Clear();
                }
                return;
            }

            // ── Hat ──────────────────────────────────────────────────────────
            var desired = new HashSet<ushort>();

            if (state.HatSwitch != -1)
            {
                int sector     = (int)Math.Round(state.HatSwitch / 4500.0) % 8;
                int normalized = sector * 4500;
                if (mapping.HatMappings.TryGetValue(normalized, out var hatKey) && hatKey != 0)
                    desired.Add(hatKey);
            }

            if (transmitting)
            {
                foreach (var key in desired)
                    if (!held.Contains(key)) KeySender.KeyDown(key);
                foreach (var key in new HashSet<ushort>(held))
                    if (!desired.Contains(key)) KeySender.KeyUp(key);
            }
            else
            {
                foreach (var key in desired)
                    if (!held.Contains(key)) keyMuted?.Invoke(key);
            }
            held.Clear();
            foreach (var key in desired) held.Add(key);

            // ── Buttons (with modifier support) ──────────────────────────────
            for (int i = 0; i < state.Buttons.Length; i++)
            {
                mapping.ButtonMappings.TryGetValue(i, out ushort btnKey);
                bool isPressed = state.Buttons[i] && btnKey != 0;
                bool wasHeld   = heldBtns.ContainsKey(i);

                if (isPressed && !wasHeld)
                {
                    mapping.ButtonModifiers.TryGetValue(i, out var mod);
                    if (transmitting)
                    {
                        if (mod != 0) KeySender.KeyDown(mod);
                        KeySender.KeyDown(btnKey);
                    }
                    else keyMuted?.Invoke(btnKey);
                    heldBtns[i] = (btnKey, mod);
                }
                else if (!isPressed && wasHeld)
                {
                    var (key, mod) = heldBtns[i];
                    if (transmitting)
                    {
                        KeySender.KeyUp(key);
                        if (mod != 0) KeySender.KeyUp(mod);
                    }
                    heldBtns.Remove(i);
                }
            }

            // ── Axis mappings ────────────────────────────────────────────────
            int axCount = Math.Min(mapping.AxisMappings.Count, Math.Min(prevSteps.Length, cpHeld.Length));
            for (int i = 0; i < axCount; i++)
            {
                var axMap  = mapping.AxisMappings[i];
                int holdMs = axMap.KeyHoldMs;
                int range  = axMap.CalMax - axMap.CalMin;
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
                        int diff     = curStep - prevSteps[i];
                        int prevStep = prevSteps[i];
                        prevSteps[i] = curStep;

                        if (axMap.UseCenter && axMap.CenterKey != 0 && prevStep != 0 && curStep == 0)
                        {
                            ushort key = axMap.CenterKey;
                            if (transmitting)
                                _ = Task.Run(async () => { KeySender.KeyDown(key); await Task.Delay(holdMs); KeySender.KeyUp(key); });
                            else
                                keyMuted?.Invoke(key);
                            axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ○ CENTER", axMap.CenterKey);
                        }

                        // CP has priority in its own zone only — allow steps when returning from the opposite zone
                        bool stepUpAllowed   = !axMap.UseCp || axMap.CpUpKey   == 0 || prevStep < 0;
                        bool stepDownAllowed = !axMap.UseCp || axMap.CpDownKey == 0 || prevStep > 0;

                        if (diff > 0 && axMap.UpKey != 0 && stepUpAllowed)
                        {
                            ushort key = axMap.UpKey; int count = diff;
                            if (transmitting)
                            {
                                if (queues?[i] is { } q)
                                {
                                    int pauseMs = axMap.KeyPauseMs > 0 ? axMap.KeyPauseMs : holdMs;
                                    for (int n = 0; n < count; n++) q.Enqueue(key, holdMs, pauseMs);
                                }
                                else
                                {
                                    _ = Task.Run(async () => {
                                        for (int n = 0; n < count; n++)
                                        {
                                            KeySender.KeyDown(key);
                                            await Task.Delay(holdMs);
                                            KeySender.KeyUp(key);
                                            if (n < count - 1) await Task.Delay(16);
                                        }
                                    });
                                }
                            }
                            else
                            {
                                for (int n = 0; n < count; n++) keyMuted?.Invoke(key);
                            }
                            for (int n = 0; n < diff; n++)
                                axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▲", axMap.UpKey);
                        }
                        else if (diff < 0 && axMap.DownKey != 0 && stepDownAllowed)
                        {
                            ushort key = axMap.DownKey; int count = -diff;
                            if (transmitting)
                            {
                                if (queues?[i] is { } q)
                                {
                                    int pauseMs = axMap.KeyPauseMs > 0 ? axMap.KeyPauseMs : holdMs;
                                    for (int n = 0; n < count; n++) q.Enqueue(key, holdMs, pauseMs);
                                }
                                else
                                {
                                    _ = Task.Run(async () => {
                                        for (int n = 0; n < count; n++)
                                        {
                                            KeySender.KeyDown(key);
                                            await Task.Delay(holdMs);
                                            KeySender.KeyUp(key);
                                            if (n < count - 1) await Task.Delay(16);
                                        }
                                    });
                                }
                            }
                            else
                            {
                                for (int n = 0; n < count; n++) keyMuted?.Invoke(key);
                            }
                            for (int n = 0; n < -diff; n++)
                                axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▼", axMap.DownKey);
                        }

                        // ── End-of-travel keypresses ──────────────────────────
                        int maxStep = axMap.UseCenter ? axMap.StepsUp   : axMap.StepsUp;
                        int minStep = axMap.UseCenter ? -axMap.StepsDown : 0;

                        if (axMap.MaxKey != 0 && curStep == maxStep && prevStep < maxStep)
                        {
                            ushort key = axMap.MaxKey;
                            if (transmitting)
                            {
                                int pauseMs = axMap.KeyPauseMs > 0 ? axMap.KeyPauseMs : holdMs;
                                if (queues?[i] is { } q) q.Enqueue(key, holdMs, pauseMs);
                                else _ = Task.Run(async () => { KeySender.KeyDown(key); await Task.Delay(holdMs); KeySender.KeyUp(key); });
                            }
                            else
                                keyMuted?.Invoke(key);
                            axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▲ MAX", key);
                        }
                        else if (axMap.MinKey != 0 && curStep == minStep && prevStep > minStep)
                        {
                            ushort key = axMap.MinKey;
                            if (transmitting)
                            {
                                int pauseMs = axMap.KeyPauseMs > 0 ? axMap.KeyPauseMs : holdMs;
                                if (queues?[i] is { } q) q.Enqueue(key, holdMs, pauseMs);
                                else _ = Task.Run(async () => { KeySender.KeyDown(key); await Task.Delay(holdMs); KeySender.KeyUp(key); });
                            }
                            else
                                keyMuted?.Invoke(key);
                            axisStepSent?.Invoke(state.DeviceGuid, $"{axMap.Label} ▼ MIN", key);
                        }
                    }
                }

                // ── Constant Pressure mode (held keys) ────────────────────────
                if (axMap.UseCp && transmitting)
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

        private StepQueue?[] GetOrCreateQueues(string guid, List<AxisStepMapping> axMaps)
        {
            if (!_queues.TryGetValue(guid, out var queues) || queues.Length != axMaps.Count)
            {
                if (queues != null)
                    foreach (var q in queues) q?.Dispose();
                queues = new StepQueue?[axMaps.Count];
                _queues[guid] = queues;
            }
            for (int i = 0; i < axMaps.Count; i++)
            {
                if (axMaps[i].StackedMode && queues[i] == null)
                    queues[i] = new StepQueue();
                else if (!axMaps[i].StackedMode && queues[i] != null)
                {
                    queues[i]!.Dispose();
                    queues[i] = null;
                }
            }
            return queues;
        }

        private void DisposeAllQueues()
        {
            foreach (var arr in _queues.Values)
                foreach (var q in arr) q?.Dispose();
            _queues.Clear();
        }

        public void Dispose() => DisposeAllQueues();

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

        private static void ReleaseBtns(Dictionary<int, (ushort key, ushort mod)> heldBtns)
        {
            foreach (var (key, mod) in heldBtns.Values)
            {
                KeySender.KeyUp(key);
                if (mod != 0) KeySender.KeyUp(mod);
            }
            heldBtns.Clear();
        }

        private void ReleaseBtnsAll()
        {
            foreach (var btns in _heldBtns.Values)
                ReleaseBtns(btns);
        }

        public void ReleaseAll()
        {
            foreach (var held in _held.Values) ReleaseHeld(held);
            ReleaseCpAll();
            ReleaseBtnsAll();
        }
    }
}

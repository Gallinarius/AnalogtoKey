using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;
using Nefarius.Drivers.HidHide;

namespace AnalogtoKey.Services;

public class HidHideService : IDisposable
{
    private readonly HidHideControlService? _svc;
    private readonly List<string> _hiddenInstances = new();
    private bool _isHiding;

    public bool IsAvailable { get; }
    public bool IsHiding => _isHiding;

    public HidHideService()
    {
        try
        {
            _svc = new HidHideControlService();
            IsAvailable = _svc.IsInstalled;
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public void WhitelistSelf()
    {
        if (!IsAvailable || _svc == null) return;
        try
        {
            var exe = Process.GetCurrentProcess().MainModule!.FileName;
            _svc.AddApplicationPath(exe, throwIfInvalid: false);
        }
        catch { }
    }

    public void HideDevices(IEnumerable<string> vids)
    {
        if (!IsAvailable || _svc == null) return;
        foreach (var id in FindInstanceIds(vids))
        {
            try
            {
                _svc.AddBlockedInstanceId(id);
                if (!_hiddenInstances.Contains(id))
                    _hiddenInstances.Add(id);
            }
            catch { }
        }
        try { _svc.IsActive = true; } catch { }
        _isHiding = true;
    }

    public void RestoreDevices()
    {
        if (!IsAvailable || _svc == null || !_isHiding) return;
        foreach (var id in _hiddenInstances)
            try { _svc.RemoveBlockedInstanceId(id); } catch { }
        _hiddenInstances.Clear();
        try
        {
            var exe = Process.GetCurrentProcess().MainModule!.FileName;
            _svc.RemoveApplicationPath(exe);
        }
        catch { }
        try { _svc.IsActive = false; } catch { }
        _isHiding = false;
    }

    private static List<string> FindInstanceIds(IEnumerable<string> vids)
    {
        var vidSet = new HashSet<string>(vids, StringComparer.OrdinalIgnoreCase);
        var results = new List<string>();
        foreach (var bus in new[] { "HID", "USB" })
            CollectInstanceIds(bus, vidSet, results);
        return results;
    }

    private static void CollectInstanceIds(string bus, HashSet<string> vids, List<string> results)
    {
        using var busKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{bus}");
        if (busKey == null) return;
        foreach (var vidPid in busKey.GetSubKeyNames())
        {
            // Match på VID uanset PID og interface-suffix
            if (!vids.Any(v => vidPid.Contains(v, StringComparison.OrdinalIgnoreCase))) continue;
            using var vidPidKey = busKey.OpenSubKey(vidPid);
            if (vidPidKey == null) continue;
            foreach (var instance in vidPidKey.GetSubKeyNames())
                results.Add($@"{bus}\{vidPid}\{instance}");
        }
    }

    public void Dispose()
    {
        if (_isHiding) RestoreDevices();
    }
}

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace AnalogtoKey
{
    public partial class DebugKeyWindow : Window
    {
        // ── P/Invoke ──────────────────────────────────────────────────────────
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc fn, IntPtr hMod, uint threadId);
        [DllImport("user32.dll")] static extern bool   UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string? lpModuleName);

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

        private const int    WH_KEYBOARD_LL  = 13;
        private const int    WM_KEYDOWN      = 0x0100;
        private const int    WM_SYSKEYDOWN   = 0x0104;
        private const uint   LLKHF_INJECTED  = 0x10;

        // ── Fields ────────────────────────────────────────────────────────────
        private readonly LowLevelKeyboardProc _hookProc; // must be kept alive
        private IntPtr _hook = IntPtr.Zero;
        private bool   _modifiersOnly;

        private static readonly SolidColorBrush ColGreen = new(Color.FromRgb(0, 255, 153));
        private static readonly SolidColorBrush ColRed   = new(Color.FromRgb(255, 80, 80));

        // ── Constructor ───────────────────────────────────────────────────────
        public DebugKeyWindow()
        {
            InitializeComponent();
            LogBox.Document.PagePadding = new Thickness(0);
            _hookProc = HookCallback;
            InstallHook();
            Closed += (_, _) => RemoveHook();
        }

        // ── Hook install / remove ─────────────────────────────────────────────
        private void InstallHook()
        {
            using var proc = System.Diagnostics.Process.GetCurrentProcess();
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc,
                GetModuleHandle(proc.MainModule?.ModuleName), 0);
        }

        private void RemoveHook()
        {
            if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }

        // ── Hook callback ─────────────────────────────────────────────────────
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var info      = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                bool isDown   = wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN;
                bool synthetic = (info.flags & LLKHF_INJECTED) != 0;
                bool isMod    = IsModifier(info.vkCode);

                if (!_modifiersOnly || isMod)
                {
                    var line = $"{DateTime.Now:HH:mm:ss.fff}  {(synthetic ? "[S]" : "[P]")}  " +
                               $"{VkName(info.vkCode),-12} {(isDown ? "DOWN" : "UP")}";
                    Dispatcher.BeginInvoke(() => AppendLine(line, ColGreen));
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static bool IsModifier(uint vk) =>
            vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C   // Shift Ctrl Alt LWin RWin
               or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5; // L/R variants

        private static string VkName(uint vk) => vk switch
        {
            0x08 => "Back",      0x09 => "Tab",     0x0D => "Enter",
            0x10 => "Shift",     0x11 => "Ctrl",    0x12 => "Alt",
            0x1B => "Escape",    0x20 => "Space",
            0x25 => "Left",      0x26 => "Up",      0x27 => "Right",  0x28 => "Down",
            0x21 => "PageUp",    0x22 => "PageDown", 0x23 => "End",   0x24 => "Home",
            0x2E => "Delete",
            0x5B => "LWin",      0x5C => "RWin",
            0xA0 => "LShift",    0xA1 => "RShift",
            0xA2 => "LCtrl",     0xA3 => "RCtrl",
            0xA4 => "LAlt",      0xA5 => "RAlt",
            >= 0x41 and <= 0x5A => ((char)vk).ToString(),
            >= 0x30 and <= 0x39 => ((char)vk).ToString(),
            >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
            _ => $"0x{vk:X2}"
        };

        // ── Public API ────────────────────────────────────────────────────────
        public void AppendMuted(ushort vk)
        {
            var line = $"{DateTime.Now:HH:mm:ss.fff}  [M]  {VkName(vk),-12} DOWN";
            Dispatcher.BeginInvoke(() => AppendLine(line, ColRed));
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void AppendLine(string text, SolidColorBrush color)
        {
            var para = new Paragraph(new Run(text))
            {
                Foreground  = color,
                Margin      = new Thickness(0),
                LineHeight  = 16
            };
            LogBox.Document.Blocks.Add(para);
            LogBox.ScrollToEnd();
        }

        // ── UI events ─────────────────────────────────────────────────────────
        private void Clear_Click(object sender, RoutedEventArgs e)  => LogBox.Document.Blocks.Clear();
        private void Filter_Changed(object sender, RoutedEventArgs e) => _modifiersOnly = ModifiersOnlyCheckbox.IsChecked == true;
    }
}

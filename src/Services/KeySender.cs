using System;
using System.Runtime.InteropServices;

namespace AnalogtoKey.Services
{
    public static class KeySender
    {
        [DllImport("user32.dll")]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Korrekt 64-bit struct-layout: union skal inkludere MOUSEINPUT
        // så den samlede størrelse er korrekt (Windows afviser kaldet ellers lydløst)
        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;   // størst — 28 bytes på 64-bit
            [FieldOffset(0)] public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        public static void KeyDown(ushort vkCode) => SendKey(vkCode, false);
        public static void KeyUp(ushort vkCode)   => SendKey(vkCode, true);

        private static void SendKey(ushort vkCode, bool keyUp)
        {
            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                Data = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = vkCode,
                        wScan = 0,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };
            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }
    }

    // Virtual key codes vi bruger i JoyMap
    public static class VKey
    {
        public const ushort None = 0x00;
        public const ushort Back = 0x08;
        public const ushort Tab = 0x09;
        public const ushort Return = 0x0D;
        public const ushort Shift = 0x10;
        public const ushort Control = 0x11;
        public const ushort Alt = 0x12;
        public const ushort Escape = 0x1B;
        public const ushort Space = 0x20;
        public const ushort Prior = 0x21;   // Page Up
        public const ushort Next = 0x22;    // Page Down
        public const ushort End = 0x23;
        public const ushort Home = 0x24;
        public const ushort Left = 0x25;
        public const ushort Up = 0x26;
        public const ushort Right = 0x27;
        public const ushort Down = 0x28;
        public const ushort Delete = 0x2E;
        public const ushort D0 = 0x30;
        public const ushort D1 = 0x31;
        public const ushort D2 = 0x32;
        public const ushort D3 = 0x33;
        public const ushort D4 = 0x34;
        public const ushort D5 = 0x35;
        public const ushort D6 = 0x36;
        public const ushort D7 = 0x37;
        public const ushort D8 = 0x38;
        public const ushort D9 = 0x39;
        public const ushort A = 0x41;
        public const ushort B = 0x42;
        public const ushort C = 0x43;
        public const ushort D = 0x44;
        public const ushort E = 0x45;
        public const ushort F = 0x46;
        public const ushort G = 0x47;
        public const ushort H = 0x48;
        public const ushort I = 0x49;
        public const ushort J = 0x4A;
        public const ushort K = 0x4B;
        public const ushort L = 0x4C;
        public const ushort M = 0x4D;
        public const ushort N = 0x4E;
        public const ushort O = 0x4F;
        public const ushort P = 0x50;
        public const ushort Q = 0x51;
        public const ushort R = 0x52;
        public const ushort S = 0x53;
        public const ushort T = 0x54;
        public const ushort U = 0x55;
        public const ushort V = 0x56;
        public const ushort W = 0x57;
        public const ushort X = 0x58;
        public const ushort Y = 0x59;
        public const ushort Z = 0x5A;
        public const ushort F1 = 0x70;
        public const ushort F2 = 0x71;
        public const ushort F3 = 0x72;
        public const ushort F4 = 0x73;
        public const ushort F5 = 0x74;
        public const ushort F6 = 0x75;
        public const ushort F7 = 0x76;
        public const ushort F8 = 0x77;
        public const ushort F9 = 0x78;
        public const ushort F10 = 0x79;
        public const ushort F11 = 0x7A;
        public const ushort F12 = 0x7B;
        public const ushort NumPad0 = 0x60;
        public const ushort NumPad1 = 0x61;
        public const ushort NumPad2 = 0x62;
        public const ushort NumPad3 = 0x63;
        public const ushort NumPad4 = 0x64;
        public const ushort NumPad5 = 0x65;
        public const ushort NumPad6 = 0x66;
        public const ushort NumPad7 = 0x67;
        public const ushort NumPad8 = 0x68;
        public const ushort NumPad9 = 0x69;
        public const ushort OemMinus = 0xBD;
        public const ushort OemPlus = 0xBB;
        public const ushort OemComma = 0xBC;
        public const ushort OemPeriod = 0xBE;
    }
}

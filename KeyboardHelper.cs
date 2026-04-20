using System;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace VirtualKeyboard
{
    public static class KeyboardHelper
    {
        // Windows API 导入
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern short VkKeyScan(char ch);

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // 发送按键
        public static void SendKey(byte virtualKey, bool shift = false, bool ctrl = false, bool alt = false)
        {
            // 按下修饰键
            if (shift) keybd_event(0x10, 0, 0, UIntPtr.Zero); // VK_SHIFT
            if (ctrl) keybd_event(0x11, 0, 0, UIntPtr.Zero);  // VK_CONTROL
            if (alt) keybd_event(0x12, 0, 0, UIntPtr.Zero);   // VK_MENU

            // 按下目标键
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
            // 释放目标键
            keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);

            // 释放修饰键
            if (alt) keybd_event(0x12, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (ctrl) keybd_event(0x11, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            if (shift) keybd_event(0x10, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // 发送修饰键释放事件
        public static void SendKeyRelease(byte virtualKey)
        {
            keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // 只发送key down（用于重复按键）
        public static void SendKeyDown(byte virtualKey)
        {
            keybd_event(virtualKey, 0, 0, UIntPtr.Zero);
        }

        // 只发送key up（用于释放按键）
        public static void SendKeyUp(byte virtualKey)
        {
            keybd_event(virtualKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        // 发送字符（支持Ctrl/Alt修饰键）
        public static void SendChar(char ch, bool ctrl = false, bool alt = false)
        {
            short vk = VkKeyScan(ch);
            byte virtualKey = (byte)(vk & 0xFF);
            bool shift = (vk & 0x100) != 0;

            SendKey(virtualKey, shift, ctrl, alt);
        }

        // 虚拟键码常量
        public static class VK
        {
            public const byte BACK = 0x08;      // Backspace
            public const byte TAB = 0x09;       // Tab
            public const byte RETURN = 0x0D;    // Enter
            public const byte SHIFT = 0x10;     // Shift
            public const byte CONTROL = 0x11;   // Ctrl
            public const byte MENU = 0x12;      // Alt
            public const byte CAPITAL = 0x14;   // Caps Lock
            public const byte ESCAPE = 0x1B;    // Esc
            public const byte SPACE = 0x20;     // Space
            public const byte LEFT = 0x25;      // ←
            public const byte UP = 0x26;        // ↑
            public const byte RIGHT = 0x27;     // →
            public const byte DOWN = 0x28;      // ↓
            
            // 数字键 0-9
            public const byte KEY_0 = 0x30;
            public const byte KEY_1 = 0x31;
            public const byte KEY_2 = 0x32;
            public const byte KEY_3 = 0x33;
            public const byte KEY_4 = 0x34;
            public const byte KEY_5 = 0x35;
            public const byte KEY_6 = 0x36;
            public const byte KEY_7 = 0x37;
            public const byte KEY_8 = 0x38;
            public const byte KEY_9 = 0x39;
            
            // 字母键 A-Z
            public const byte A = 0x41;
            public const byte B = 0x42;
            public const byte C = 0x43;
            public const byte D = 0x44;
            public const byte E = 0x45;
            public const byte F = 0x46;
            public const byte G = 0x47;
            public const byte H = 0x48;
            public const byte I = 0x49;
            public const byte J = 0x4A;
            public const byte K = 0x4B;
            public const byte L = 0x4C;
            public const byte M = 0x4D;
            public const byte N = 0x4E;
            public const byte O = 0x4F;
            public const byte P = 0x50;
            public const byte Q = 0x51;
            public const byte R = 0x52;
            public const byte S = 0x53;
            public const byte T = 0x54;
            public const byte U = 0x55;
            public const byte V = 0x56;
            public const byte W = 0x57;
            public const byte X = 0x58;
            public const byte Y = 0x59;
            public const byte Z = 0x5A;
            
            // 符号键
            public const byte OEM_1 = 0xBA;     // ;:
            public const byte OEM_PLUS = 0xBB;  // =+
            public const byte OEM_COMMA = 0xBC; // ,<
            public const byte OEM_MINUS = 0xBD; // -_
            public const byte OEM_PERIOD = 0xBE;// .>
            public const byte OEM_2 = 0xBF;     // /?
            public const byte OEM_3 = 0xC0;     // `~
            public const byte OEM_4 = 0xDB;     // [{
            public const byte OEM_5 = 0xDC;     // \|
            public const byte OEM_6 = 0xDD;     // ]}
            public const byte OEM_7 = 0xDE;     // '"
        }
    }
}

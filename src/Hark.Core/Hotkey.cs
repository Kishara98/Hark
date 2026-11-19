using System;
using System.Collections.Generic;
using System.Globalization;

namespace Hark.Core
{
    /// <summary>Values match the Win32 MOD_ constants used by RegisterHotKey.</summary>
    [Flags]
    public enum HotkeyModifiers
    {
        None = 0,
        Alt = 1,
        Control = 2,
        Shift = 4,
        Win = 8,
    }

    /// <summary>A global hotkey such as "Ctrl+Alt+R": at least one modifier plus A-Z, 0-9 or F1-F12.</summary>
    public readonly struct Hotkey
    {
        private const int VkF1 = 0x70;

        public Hotkey(HotkeyModifiers modifiers, int virtualKey)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
        }

        public HotkeyModifiers Modifiers { get; }

        public int VirtualKey { get; }

        public static bool TryParse(string text, out Hotkey hotkey)
        {
            hotkey = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            HotkeyModifiers modifiers = HotkeyModifiers.None;
            int? key = null;
            foreach (string rawPart in text.Split('+'))
            {
                string part = rawPart.Trim().ToUpperInvariant();
                HotkeyModifiers modifier = ParseModifier(part);
                if (modifier != HotkeyModifiers.None)
                {
                    modifiers |= modifier;
                    continue;
                }

                int? parsedKey = ParseKey(part);
                if (parsedKey == null || key != null)
                {
                    return false;
                }

                key = parsedKey;
            }

            if (key == null || modifiers == HotkeyModifiers.None)
            {
                return false;
            }

            hotkey = new Hotkey(modifiers, key.Value);
            return true;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if ((Modifiers & HotkeyModifiers.Control) != 0) parts.Add("Ctrl");
            if ((Modifiers & HotkeyModifiers.Alt) != 0) parts.Add("Alt");
            if ((Modifiers & HotkeyModifiers.Shift) != 0) parts.Add("Shift");
            if ((Modifiers & HotkeyModifiers.Win) != 0) parts.Add("Win");
            parts.Add(VirtualKey >= VkF1
                ? "F" + (VirtualKey - VkF1 + 1).ToString(CultureInfo.InvariantCulture)
                : ((char)VirtualKey).ToString());
            return string.Join("+", parts);
        }

        private static HotkeyModifiers ParseModifier(string part)
        {
            switch (part)
            {
                case "CTRL":
                case "CONTROL":
                    return HotkeyModifiers.Control;
                case "ALT":
                    return HotkeyModifiers.Alt;
                case "SHIFT":
                    return HotkeyModifiers.Shift;
                case "WIN":
                    return HotkeyModifiers.Win;
                default:
                    return HotkeyModifiers.None;
            }
        }

        private static int? ParseKey(string part)
        {
            if (part.Length == 1 && ((part[0] >= 'A' && part[0] <= 'Z') || (part[0] >= '0' && part[0] <= '9')))
            {
                return part[0];
            }

            if (part.Length >= 2 && part[0] == 'F'
                && int.TryParse(part.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number >= 1 && number <= 12)
            {
                return VkF1 + number - 1;
            }

            return null;
        }
    }
}

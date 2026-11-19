using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Hark.App
{
    /// <summary>The eight agent pets from board "H · Agent pets": line art on a 24 x 24 grid, each with its colour.</summary>
    internal static class Pets
    {
        private static readonly string[] Paths =
        {
            // Fox
            "M4 4l3.5 5.5h9L20 4 M4 4l1 8c.8 4.6 3.6 8 7 8s6.2-3.4 7-8l1-8 M9 12.5h.01 M15 12.5h.01 M11 16l1 1 1-1",
            // Owl
            "M6 4l2 3h8l2-3 M5 9c0-1.5 1.5-2 3-2h8c1.5 0 3 .5 3 2v6c0 3.5-3 6-7 6s-7-2.5-7-6z M7.5 11a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M12.5 11a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M11.2 14.5l.8 1.2.8-1.2",
            // Cat
            "M5 10V4l4 3h6l4-3v6c0 5-3.1 9-7 9s-7-4-7-9z M9.5 12h.01 M14.5 12h.01 M11 15h2l-1 1z M2.5 13.5h3 M2.5 16l3-1 M21.5 13.5h-3 M21.5 16l-3-1",
            // Bunny
            "M9 9.5V3.8a1.6 1.6 0 0 1 3.2 0V9 M12 9V4.6a1.6 1.6 0 0 1 3.2 0v5 M6 14.5a6 6 0 1 0 12 0a6 6 0 1 0 -12 0 M10 14h.01 M14 14h.01 M11.2 16.5l.8.6.8-.6",
            // Panda
            "M5 13a7 7 0 1 0 14 0a7 7 0 1 0 -14 0 M4 6.5a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M16 6.5a2 2 0 1 0 4 0a2 2 0 1 0 -4 0 M7.6 12a1.6 2 0 1 0 3.2 0a1.6 2 0 1 0 -3.2 0 M13.2 12a1.6 2 0 1 0 3.2 0a1.6 2 0 1 0 -3.2 0 M11 16h2",
            // Frog
            "M4 14c0-3.5 3.6-5.5 8-5.5s8 2 8 5.5-3.6 6-8 6-8-2.5-8-6z M5.5 8a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0 -5 0 M13.5 8a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0 -5 0 M8 8h.01 M16 8h.01 M8.5 15c2 1.5 5 1.5 7 0",
            // Penguin
            "M12 3c-3.6 0-6 3.6-6 8.5S8 21 12 21s6-4.6 6-9.5S15.6 3 12 3z M9 11.5c0 3 1.3 6 3 6s3-3 3-6-1.3-3.5-3-3.5-3 .5-3 3.5z M10 7.5h.01 M14 7.5h.01 M11 9.5l1 1 1-1",
            // Hedgehog
            "M4 15c0-4.5 3.5-8 8-8 2.5 0 4.7 1.1 6 3l2 2.5-2 .5c0 3-2.7 5-6 5H6c-1.1 0-2-1.3-2-3z M6 9.5 4.5 7.5 M9 7.8 8.5 5.3 M12.5 7l.5-2.5 M16 8l1.5-2 M16 11.5h.01 M8 18v2 M13 18v2",
        };

        private static readonly uint[] Colors = { 0xfb923c, 0xa78bfa, 0xf472b6, 0x93c5fd, 0xe4e4e7, 0x4ade80, 0x67e8f9, 0xfbbf24 };

        public static Brush Color(int pet) => Theme.Hex(Colors[pet % Colors.Length]);

        public static Brush Tint(int pet) => Theme.Hex(Colors[pet % Colors.Length], 0x29);

        /// <summary>The pet in a tinted circle.</summary>
        public static FrameworkElement Avatar(int pet, double size)
        {
            return new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = Tint(pet),
                Child = Theme.Icon(Paths[pet % Paths.Length], size * 0.63, Color(pet), 1.6),
            };
        }
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Hark.App
{
    /// <summary>Colours, fonts and sizes from board E (see plan Global Constraints).</summary>
    internal static class Theme
    {
        public static readonly Brush Ink = Hex(0x09090b);
        public static readonly Brush Text2 = Hex(0x52525b);
        public static readonly Brush Muted = Hex(0x71717a);
        public static readonly Brush Line = Hex(0xe4e4e7);
        public static readonly Brush ChipFill = Hex(0xfafafa);
        public static readonly Brush ChipText = Hex(0x27272a);
        public static readonly Brush ButtonText = Hex(0x18181b);
        public static readonly Brush WaitFill = Hex(0xfef3c7);
        public static readonly Brush WaitText = Hex(0x92400e);
        public static readonly Brush WaitDot = Hex(0xf59e0b);
        public static readonly Brush WaitRing = Hex(0xf59e0b, 0x47);
        public static readonly Brush DoneFill = Hex(0xf4f4f5);
        public static readonly Brush DoneText = Hex(0x3f3f46);
        public static readonly Brush Spinner = Hex(0x60a5fa);
        public static readonly Brush PillDivider = Hex(0x3f3f46);
        public static readonly Brush PillMuted = Hex(0xa1a1aa);
        public static readonly Brush White = Brushes.White;

        public static readonly FontFamily Sans = new FontFamily(new Uri(AppPaths.FontsDir), "./#Geist, Segoe UI");
        public static readonly FontFamily Mono = new FontFamily(new Uri(AppPaths.FontsDir), "./#Geist Mono, Consolas");

        public const double CardWidth = 380;
        public const double ShadowRoom = 24;

        // Lucide style icons (24 x 24 grid, stroke only)
        public const string EyeIcon = "M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z M9 12a3 3 0 1 0 6 0a3 3 0 1 0 -6 0";
        public const string CloseIcon = "M18 6 6 18 M6 6l12 12";
        public const string SpinnerIcon = "M21 12a9 9 0 1 1-6.2-8.6";

        public static SolidColorBrush Hex(uint rgb, byte alpha = 0xff)
        {
            var b = new SolidColorBrush(Color.FromArgb(alpha, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
            b.Freeze();
            return b;
        }

        public static Effect Shadow() => new DropShadowEffect
        {
            BlurRadius = 40,
            ShadowDepth = 10,
            Direction = 270,
            Opacity = 0.22,
            Color = Colors.Black,
        };

        public static TextBlock Text(string text, double size, Brush color, FontWeight weight, FontFamily family = null) => new TextBlock
        {
            Text = text ?? "",
            FontSize = size,
            Foreground = color,
            FontWeight = weight,
            FontFamily = family ?? Sans,
            VerticalAlignment = VerticalAlignment.Center,
        };

        public static FrameworkElement Icon(string data, double size, Brush stroke, double thickness = 1.75)
        {
            var path = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(data),
                Stroke = stroke,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
            };
            var canvas = new Canvas { Width = 24, Height = 24 };
            canvas.Children.Add(path);
            return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center };
        }

        public static Button Button(string name, object content, Brush fill, Brush border, Brush text, double height, double radius, Thickness padding, Action onClick)
        {
            var chrome = new FrameworkElementFactory(typeof(Border));
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            chrome.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.85));
            template.Triggers.Add(hover);

            var button = new Button
            {
                Content = content,
                Template = template,
                Background = fill ?? Brushes.Transparent,
                BorderBrush = border ?? Brushes.Transparent,
                BorderThickness = new Thickness(border == null ? 0 : 1),
                Foreground = text,
                Height = height,
                Padding = padding,
                Focusable = false,
                Cursor = System.Windows.Input.Cursors.Hand,
                FontFamily = Sans,
            };
            System.Windows.Automation.AutomationProperties.SetName(button, name);
            button.Click += (s, e) => onClick();
            return button;
        }
    }
}

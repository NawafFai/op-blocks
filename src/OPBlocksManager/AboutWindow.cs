using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace OPBlocksManager
{
    /// <summary>
    /// About dialog — credits Engineer Nawaf / ONE PROCESS Simulation. Built in
    /// code (no XAML) so it can be composed directly from the shared
    /// <see cref="Localizer"/> and render in EN or AR (with RTL) as selected.
    /// </summary>
    public sealed class AboutWindow : Window
    {
        private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(0x0E, 0x7C, 0x66));

        public AboutWindow(Localizer l, string version)
        {
            Title = l["AboutTitle"];
            Width = 460;
            Height = 340;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.White;
            FlowDirection = l.IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            FontFamily = new FontFamily("Segoe UI");

            var root = new DockPanel();

            // Header
            var header = new Border { Background = Accent, Height = 92 };
            var hstack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 20, 0) };
            hstack.Children.Add(BuildHex());
            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
            titleStack.Children.Add(new TextBlock { Text = l["AppTitle"], Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.Bold });
            titleStack.Children.Add(new TextBlock { Text = l["AboutTagline"], Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0xEF, 0xEA)), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxWidth = 300 });
            hstack.Children.Add(titleStack);
            header.Child = hstack;
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            // Footer (close)
            var footer = new Border { Height = 56, Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF5)) };
            var close = new Button
            {
                Content = l["AboutClose"],
                Width = 120,
                Height = 32,
                Margin = new Thickness(16),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Background = Accent,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            close.Click += (s, e) => Close();
            footer.Child = close;
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            // Body
            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 12) };
            body.Children.Add(Row(l["AboutMadeBy"], 15, FontWeights.SemiBold, Color.FromRgb(0x22, 0x30, 0x2C)));
            body.Children.Add(Row(l["AboutOrg"], 13, FontWeights.Normal, Color.FromRgb(0x0E, 0x7C, 0x66)));
            body.Children.Add(new Separator { Margin = new Thickness(0, 12, 0, 12), Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xE3, 0xE1)) });
            body.Children.Add(Row(l["AboutVersion"] + ": " + version, 12, FontWeights.Normal, Color.FromRgb(0x5B, 0x6A, 0x66)));
            body.Children.Add(Row("© ONE PROCESS Simulation", 11, FontWeights.Normal, Color.FromRgb(0x8A, 0x96, 0x8F)));
            root.Children.Add(body);

            Content = root;
        }

        private static TextBlock Row(string text, double size, FontWeight weight, Color color)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                FontWeight = weight,
                Foreground = new SolidColorBrush(color),
                Margin = new Thickness(0, 3, 0, 3),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private static UIElement BuildHex()
        {
            var grid = new Grid { Width = 52, Height = 52 };
            var hex = new Polygon
            {
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Fill = Brushes.Transparent,
                Stretch = Stretch.Uniform
            };
            var pts = new PointCollection();
            for (int i = 0; i < 6; i++)
            {
                double a = System.Math.PI / 180.0 * (60 * i - 30);
                pts.Add(new Point(System.Math.Cos(a), System.Math.Sin(a)));
            }
            hex.Points = pts;
            grid.Children.Add(hex);
            grid.Children.Add(new TextBlock
            {
                Text = "OP",
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                FontSize = 13,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });
            return grid;
        }
    }
}

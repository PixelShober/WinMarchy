using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinMarchy.Core.GlazeWm;
using WinMarchy.Core.Integrations;

namespace WinMarchy.App.Overlays;

/// <summary>
/// Notifications in the top right corner, like mako in Omarchy: below the bar, never in the way of the
/// mouse, gone after a few seconds. At most four at a time.
/// </summary>
public static class Osd
{
    static OsdWindow? _window;

    public static void Show(string text)
    {
        var app = Application.Current;
        if (app == null || string.IsNullOrWhiteSpace(text)) return;
        app.Dispatcher.InvokeAsync(() =>
        {
            _window ??= new OsdWindow();
            _window.Add(text);
        });
    }

    sealed class OsdWindow : Window
    {
        const int MarginPx = 12;
        readonly StackPanel _stack = new();

        public OsdWindow()
        {
            Title = GlazeConfig.OverlayTitle;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Focusable = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Content = _stack;
            SourceInitialized += (_, _) => Placement.MakeOverlay(this, clickThrough: true);
            SizeChanged += (_, _) => Position();
        }

        public void Add(string text)
        {
            var look = OverlayLook.Current();
            var card = new Border
            {
                Background = look.Background,
                BorderBrush = look.Accent,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8),
                Width = 360,
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = look.Foreground,
                    FontFamily = OverlayLook.Mono,
                    FontSize = 13.5,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            _stack.Children.Add(card);
            while (_stack.Children.Count > 4) _stack.Children.RemoveAt(0);
            if (!IsVisible) Show();
            Position();
            if (Ui.Motion.Enabled) Ui.Motion.FadeIn(card, -6);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Clamp(text.Length / 18.0, 3, 8)) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                _stack.Children.Remove(card);
                if (_stack.Children.Count == 0) Hide();
            };
            timer.Start();
        }

        /// <summary>Right edge of the monitor under the mouse, below the WinMarchy bar when it is on.</summary>
        void Position()
        {
            if (!IsVisible) return;
            var area = Placement.CursorWorkArea();
            var scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            var s = AppModel.Settings;
            int top = (int)Math.Round(((s.ShowBar ? ZebarPack.BarHeight : 0) + s.OuterGap) * scale);
            int width = (int)Math.Round(ActualWidth * scale);
            Placement.MoveTopLeftPixels(this, area.Right - width - (int)Math.Round(Math.Max(s.OuterGap, MarginPx) * scale), area.Top + Math.Max(top, MarginPx));
        }
    }
}

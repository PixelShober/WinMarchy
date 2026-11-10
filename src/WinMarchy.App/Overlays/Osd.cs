using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinMarchy.Core;
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

    /// <summary>
    /// A notification with a progress bar for a running job (theme change): the bar follows the
    /// steps, the line below names the current one, and at the end it says how it went.
    /// </summary>
    public static void Follow(string jobId, string title, string done, Func<string, string> failed)
    {
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher.InvokeAsync(() =>
        {
            _window ??= new OsdWindow();
            var card = _window.AddProgress(title);
            void OnChanged(JobSnapshot snap)
            {
                if (snap.Id != jobId) return;
                app.Dispatcher.InvokeAsync(() =>
                {
                    if (snap.State == JobState.Running)
                    {
                        card.Update(snap.Progress / 100.0, snap.Status);
                        return;
                    }
                    AppModel.Jobs.Changed -= OnChanged;
                    card.Finish(snap.State == JobState.Done ? done : failed(snap.Status), snap.State == JobState.Done);
                });
            }
            AppModel.Jobs.Changed += OnChanged;
            if (AppModel.Jobs.Get(jobId)?.Snapshot() is { } now) OnChanged(now);
        });
    }

    /// <summary>The card of <see cref="Follow"/>.</summary>
    sealed class ProgressCard
    {
        public required Border Root { get; init; }
        public required TextBlock Status { get; init; }
        public required Border Fill { get; init; }
        public required Grid Track { get; init; }
        public required Action Close { get; init; }

        public void Update(double share, string status)
        {
            Status.Text = status;
            // Before the first layout pass the track has no width yet: card width minus padding and border.
            var width = Track.ActualWidth > 0 ? Track.ActualWidth : 324;
            Fill.Width = Math.Max(0, Math.Min(1, share)) * width;
        }

        public void Finish(string text, bool ok)
        {
            Update(ok ? 1 : Fill.Width / (Track.ActualWidth > 0 ? Track.ActualWidth : 324), text);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ok ? 3 : 8) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Close();
            };
            timer.Start();
        }
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

        public ProgressCard AddProgress(string title)
        {
            var look = OverlayLook.Current();
            var status = new TextBlock { Foreground = look.Muted, FontFamily = OverlayLook.Mono, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
            var fill = new Border { Background = look.Accent, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            var track = new Grid { Height = 4, Background = look.Divider, Margin = new Thickness(0, 10, 0, 0) };
            track.Children.Add(fill);
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = title, Foreground = look.Foreground, FontFamily = OverlayLook.Mono, FontSize = 13.5, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(track);
            panel.Children.Add(status);
            var card = new Border
            {
                Background = look.Background,
                BorderBrush = look.Accent,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8),
                Width = 360,
                Child = panel,
            };
            _stack.Children.Add(card);
            while (_stack.Children.Count > 4) _stack.Children.RemoveAt(0);
            if (!IsVisible) Show();
            Position();
            if (Ui.Motion.Enabled) Ui.Motion.FadeIn(card, -6);
            return new ProgressCard
            {
                Root = card, Status = status, Fill = fill, Track = track,
                Close = () =>
                {
                    _stack.Children.Remove(card);
                    if (_stack.Children.Count == 0) Hide();
                },
            };
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
            int top = (int)Math.Round(((s.ShowBar ? Bars.Height : 0) + s.OuterGap) * scale);
            int width = (int)Math.Round(ActualWidth * scale);
            Placement.MoveTopLeftPixels(this, area.Right - width - (int)Math.Round(Math.Max(s.OuterGap, MarginPx) * scale), area.Top + Math.Max(top, MarginPx));
        }
    }
}

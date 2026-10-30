using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WinMarchy.Core;

namespace WinMarchy.App;

public enum ToastKind { Ok, Info, Error }

/// <summary>
/// Short confirmations in the corner of the main window. When the window is hidden (agent in the tray),
/// they go to the Omarchy-style notification instead.
/// </summary>
public static class Toasts
{
    static Panel? _host;

    public static void Attach(Panel host) => _host = host;

    public static void Show(ToastKind kind, string text)
    {
        var app = Application.Current;
        if (app == null) return;
        app.Dispatcher.InvokeAsync(() =>
        {
            var host = _host;
            if (host == null || Window.GetWindow(host) is not { IsVisible: true } window || window.WindowState == WindowState.Minimized)
            {
                Overlays.Osd.Show(text);
                return;
            }
            var icon = kind switch { ToastKind.Ok => "check", ToastKind.Error => "alert", _ => "info" };
            var color = kind switch { ToastKind.Ok => "Ok", ToastKind.Error => "Error", _ => "AccentFg" };
            var row = new DockPanel();
            var glyph = new Ui.Icon { Kind = icon, Size = 16, Margin = new Thickness(0, 2, 10, 0), VerticalAlignment = VerticalAlignment.Top };
            glyph.SetResourceReference(Ui.Icon.ForegroundProperty, color);
            DockPanel.SetDock(glyph, Dock.Left);
            row.Children.Add(glyph);
            var close = new Button { Style = (Style)app.FindResource("IconButton"), Content = new Ui.Icon { Kind = "x", Size = 12 }, ToolTip = L.T("common.close") };
            DockPanel.SetDock(close, Dock.Right);
            row.Children.Add(close);
            var message = new TextBlock { Text = text, Style = (Style)app.FindResource("Body"), Margin = new Thickness(0, 0, 8, 0) };
            row.Children.Add(message);

            var card = new Border
            {
                Child = row,
                Padding = new Thickness(14, 10, 8, 10),
                Margin = new Thickness(0, 8, 0, 0),
                BorderThickness = new Thickness(1, 1, 1, 1),
            };
            card.SetResourceReference(Border.BackgroundProperty, "Surface");
            card.SetResourceReference(Border.BorderBrushProperty, kind == ToastKind.Error ? "Error" : "BorderStrong");
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Direction = 270, Opacity = 0.18, Color = Colors.Black };

            void Remove() => Ui.Motion.FadeOut(card, () => host.Children.Remove(card));
            close.Click += (_, _) => Remove();
            host.Children.Add(card);
            Ui.Motion.FadeIn(card, 8);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind == ToastKind.Error ? 12 : 5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Remove();
            };
            timer.Start();
            if (kind == ToastKind.Error) Log.Warn(text);
        });
    }
}

/// <summary>A confirmation inside the window (backdrop and card), for the few actions that are hard to take back.</summary>
public static class Dialogs
{
    static Grid? _host;

    public static void Attach(Grid host) => _host = host;

    public static Task<bool> ConfirmAsync(string title, string text, string confirm, bool danger = false)
    {
        var tcs = new TaskCompletionSource<bool>();
        var host = _host;
        if (host == null)
        {
            tcs.SetResult(false);
            return tcs.Task;
        }
        var app = Application.Current;
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, Style = (Style)app.FindResource("H2") });
        stack.Children.Add(new TextBlock { Text = text, Style = (Style)app.FindResource("Muted"), Margin = new Thickness(0, 8, 0, 18) });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = L.T("common.cancel"), Margin = new Thickness(0, 0, 8, 0), IsCancel = true };
        var ok = new Button { Content = confirm, Style = (Style)app.FindResource(danger ? "Danger" : "Primary"), IsDefault = true };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        stack.Children.Add(buttons);
        var card = new Border { Style = (Style)app.FindResource("Card"), Child = stack, Width = 460, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        card.SetResourceReference(Border.BorderBrushProperty, danger ? "Error" : "Accent");

        void Close(bool result)
        {
            host.Children.Clear();
            host.Visibility = Visibility.Collapsed;
            tcs.TrySetResult(result);
        }
        cancel.Click += (_, _) => Close(false);
        ok.Click += (_, _) => Close(true);
        host.Children.Add(card);
        host.Visibility = Visibility.Visible;
        Ui.Motion.FadeIn(card, 6);
        cancel.Focus();
        return tcs.Task;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace WinMarchy.App.Ui;

/// <summary>
/// The motion of WinMarchy, taken from Ember: the old page fades in 160 ms, the new one rises 6 px
/// in 260 ms, and the marker of the active tab glides to its new place. Nothing else moves.
/// </summary>
public static class Motion
{
    /// <summary>Windows' "Show animations in Windows" setting.</summary>
    public static bool Enabled => SystemParameters.ClientAreaAnimation && !ForceOff;

    /// <summary>Screenshots in CI are taken without motion.</summary>
    public static bool ForceOff { get; set; }

    /// <summary>cubic-bezier(0.22, 0.8, 0.36, 1) – Ember's easing for arrivals.</summary>
    static readonly KeySpline Arrive = new(0.22, 0.8, 0.36, 1);

    public static readonly Duration In = TimeSpan.FromMilliseconds(260);
    public static readonly Duration Out = TimeSpan.FromMilliseconds(160);

    static DoubleAnimationUsingKeyFrames Spline(double from, double to, Duration d)
    {
        var a = new DoubleAnimationUsingKeyFrames { Duration = d };
        a.KeyFrames.Add(new DiscreteDoubleKeyFrame(from, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        a.KeyFrames.Add(new SplineDoubleKeyFrame(to, KeyTime.FromTimeSpan(d.TimeSpan), Arrive));
        return a;
    }

    /// <summary>
    /// Swaps the content of <paramref name="host"/>: a snapshot of the visible area (<paramref name="area"/>)
    /// fades out above the new page rising in.
    /// </summary>
    public static void SwapPage(ContentControl host, FrameworkElement area, Panel overlayHost, FrameworkElement next)
    {
        if (!Enabled || host.Content is not FrameworkElement old || old.ActualWidth < 1 || area.ActualWidth < 1)
        {
            host.Content = next;
            return;
        }
        var origin = area.TranslatePoint(new Point(0, 0), overlayHost);
        var dpi = VisualTreeHelper.GetDpi(area);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(area.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(area.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(area);
        bitmap.Freeze();
        var snapshot = new Image
        {
            Source = bitmap, Width = area.ActualWidth, Height = area.ActualHeight, Stretch = Stretch.Fill, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(origin.X, origin.Y, 0, 0),
        };
        overlayHost.Children.Add(snapshot);
        var fade = new DoubleAnimation(1, 0, Out) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => overlayHost.Children.Remove(snapshot);
        snapshot.BeginAnimation(UIElement.OpacityProperty, fade);

        host.Content = next;
        var shift = new TranslateTransform(0, 6);
        next.RenderTransform = shift;
        next.BeginAnimation(UIElement.OpacityProperty, Spline(0, 1, In));
        shift.BeginAnimation(TranslateTransform.YProperty, Spline(6, 0, In));
    }

    /// <summary>Moves the tab marker to a nav item (its position relative to the marker's canvas).</summary>
    public static void Glide(FrameworkElement marker, FrameworkElement target, FrameworkElement canvas, bool animate)
    {
        var top = target.TranslatePoint(new Point(0, 0), canvas).Y;
        var height = target.ActualHeight;
        if (!animate || !Enabled || double.IsNaN(Canvas.GetTop(marker)) || marker.ActualHeight < 1)
        {
            marker.BeginAnimation(Canvas.TopProperty, null);
            marker.BeginAnimation(FrameworkElement.HeightProperty, null);
            Canvas.SetTop(marker, top);
            marker.Height = height;
            return;
        }
        marker.BeginAnimation(Canvas.TopProperty, Spline(Canvas.GetTop(marker), top, In));
        marker.BeginAnimation(FrameworkElement.HeightProperty, Spline(marker.ActualHeight, height, In));
    }

    /// <summary>A short fade for overlays and toasts.</summary>
    public static void FadeIn(UIElement e, double fromY = 4)
    {
        if (!Enabled)
        {
            e.Opacity = 1;
            return;
        }
        var shift = new TranslateTransform(0, fromY);
        e.RenderTransform = shift;
        e.BeginAnimation(UIElement.OpacityProperty, Spline(0, 1, TimeSpan.FromMilliseconds(180)));
        shift.BeginAnimation(TranslateTransform.YProperty, Spline(fromY, 0, TimeSpan.FromMilliseconds(180)));
    }

    public static void FadeOut(UIElement e, Action done)
    {
        if (!Enabled)
        {
            done();
            return;
        }
        var fade = new DoubleAnimation(e.Opacity, 0, TimeSpan.FromMilliseconds(120)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => done();
        e.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}

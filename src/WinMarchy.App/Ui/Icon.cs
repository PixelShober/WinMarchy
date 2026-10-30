using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace WinMarchy.App.Ui;

/// <summary>
/// One icon set, drawn on a 16 px grid with a 1.5 px stroke, round caps – the same weight everywhere.
/// The colour comes from the surrounding text colour.
/// </summary>
public sealed class Icon : FrameworkElement
{
    static readonly Dictionary<string, Geometry> Shapes = new()
    {
        ["home"] = G("M2.5 7.2 L8 2.5 L13.5 7.2 M4 6 V13.5 H12 V6"),
        ["setup"] = G("M8 2 V10 M4.8 6.8 L8 10 L11.2 6.8 M2.5 11.5 V13.5 H13.5 V11.5"),
        ["themes"] = G("M8 2.2 C6.6 4 3.6 7.2 3.6 9.9 A4.4 4.4 0 0 0 12.4 9.9 C12.4 7.2 9.4 4 8 2.2 Z"),
        ["keyboard"] = G("M1.5 4.5 H14.5 V11.5 H1.5 Z M4.5 9 H11.5 M4.2 6.8 H4.3 M6.7 6.8 H6.8 M9.2 6.8 H9.3 M11.7 6.8 H11.8"),
        ["tiling"] = G("M2.5 2.5 H13.5 V13.5 H2.5 Z M7.5 2.5 V13.5 M7.5 8 H13.5"),
        ["undo"] = G("M3.2 6.2 H10 A3.4 3.4 0 0 1 10 13 H6 M5.7 3.6 L3.2 6.2 L5.7 8.7"),
        ["settings"] = G("M2.5 4.5 H13.5 M2.5 11.5 H13.5 M5.5 2.8 V6.2 M10.5 9.8 V13.2"),
        ["log"] = G("M5.5 4 H13.5 M5.5 8 H13.5 M5.5 12 H13.5 M2.4 4 H2.5 M2.4 8 H2.5 M2.4 12 H2.5"),
        ["monitor"] = G("M2 3 H14 V11 H2 Z M6 13.5 H10 M8 11 V13.5"),
        ["sun"] = G("M8 5 A3 3 0 1 1 7.99 5 Z M8 1.5 V2.7 M8 13.3 V14.5 M1.5 8 H2.7 M13.3 8 H14.5 M3.4 3.4 L4.2 4.2 M11.8 11.8 L12.6 12.6 M3.4 12.6 L4.2 11.8 M11.8 4.2 L12.6 3.4"),
        ["moon"] = G("M13 9.6 A5.4 5.4 0 1 1 6.4 3 A4.4 4.4 0 0 0 13 9.6 Z"),
        ["check"] = G("M3 8.5 L6.5 12 L13 4.5"),
        ["star"] = G("M8 2 L9.8 5.9 L14 6.3 L10.8 9.1 L11.7 13.3 L8 11.1 L4.3 13.3 L5.2 9.1 L2 6.3 L6.2 5.9 Z"),
        ["alert"] = G("M8 2.5 L14 13 H2 Z M8 6.5 V9.3 M8 11.2 V11.3"),
        ["info"] = G("M8 1.8 A6.2 6.2 0 1 1 7.99 1.8 Z M8 7.4 V11.5 M8 5 V5.1"),
        ["x"] = G("M4 4 L12 12 M12 4 L4 12"),
        ["refresh"] = G("M13 8 A5 5 0 1 1 11.6 4.4 M13 2.4 V5 H10.4"),
        ["download"] = G("M8 2 V10.5 M4.8 7.3 L8 10.5 L11.2 7.3 M3 13.5 H13"),
        ["external"] = G("M9.5 2.5 H13.5 V6.5 M13.5 2.5 L7.5 8.5 M11.5 9.5 V13.5 H2.5 V4.5 H6.5"),
        ["folder"] = G("M1.5 4 H6 L7.5 5.5 H14.5 V13 H1.5 Z"),
        ["play"] = G("M5 3 L13 8 L5 13 Z"),
        ["search"] = G("M7 2.5 A4.5 4.5 0 1 1 6.99 2.5 Z M10.3 10.3 L13.8 13.8"),
        ["plus"] = G("M8 3 V13 M3 8 H13"),
        ["minus"] = G("M3 8 H13"),
        ["trash"] = G("M2.5 4.5 H13.5 M6 4.5 V2.5 H10 V4.5 M4 4.5 L4.8 13.5 H11.2 L12 4.5"),
        ["pencil"] = G("M10.5 2.5 L13.5 5.5 L5.5 13.5 H2.5 V10.5 Z"),
        ["chevron"] = G("M6 3.5 L10.5 8 L6 12.5"),
        ["back"] = G("M10 3.5 L5.5 8 L10 12.5"),
        ["lock"] = G("M3.5 7 H12.5 V14 H3.5 Z M5.5 7 V5 A2.5 2.5 0 0 1 10.5 5 V7"),
        ["power"] = G("M8 1.5 V7.5 M4.6 3.9 A5.4 5.4 0 1 0 11.4 3.9"),
        ["circle"] = G("M8 3 A5 5 0 1 1 7.99 3 Z"),
        ["dash"] = G("M4.5 8 H11.5"),
        ["bar"] = G("M1.5 3 H14.5 M1.5 3 V5.5 H14.5 V3 M3 13 H13"),
        ["copy"] = G("M5.5 5.5 H13.5 V13.5 H5.5 Z M3 10.5 H2.5 V2.5 H10.5 V3"),
        ["grid"] = G("M2.5 2.5 H6.5 V6.5 H2.5 Z M9.5 2.5 H13.5 V6.5 H9.5 Z M2.5 9.5 H6.5 V13.5 H2.5 Z M9.5 9.5 H13.5 V13.5 H9.5 Z"),
        ["book"] = G("M8 4 C6.5 3 4.5 2.6 2 2.8 V12.8 C4.5 12.6 6.5 13 8 14 C9.5 13 11.5 12.6 14 12.8 V2.8 C11.5 2.6 9.5 3 8 4 Z M8 4 V14"),
        ["camera"] = G("M1.5 5 H4.5 L5.8 3 H10.2 L11.5 5 H14.5 V13 H1.5 Z M8 6.5 A2.5 2.5 0 1 1 7.99 6.5 Z"),
        ["toggle"] = G("M5.5 4.5 H10.5 A3.5 3.5 0 0 1 10.5 11.5 H5.5 A3.5 3.5 0 0 1 5.5 4.5 Z M10.5 6.5 A1.5 1.5 0 1 1 10.49 6.5 Z"),
        ["chip"] = G("M4 4 H12 V12 H4 Z M6.5 6.5 H9.5 V9.5 H6.5 Z M6 1.5 V4 M10 1.5 V4 M6 12 V14.5 M10 12 V14.5 M1.5 6 H4 M1.5 10 H4 M12 6 H14.5 M12 10 H14.5"),
        ["image"] = G("M1.5 2.5 H14.5 V13.5 H1.5 Z M1.5 11 L5.5 7 L9 10.5 L11 8.5 L14.5 12 M10.5 5 A1 1 0 1 1 10.49 5 Z"),
        ["logout"] = G("M6.5 2.5 H2.5 V13.5 H6.5 M10 4.8 L13.2 8 L10 11.2 M13.2 8 H6"),
        ["sleep"] = G("M9 2.5 H13 L9 6.5 H13 M3 8.5 H7 L3 13 H7"),
        ["wifi"] = G("M1.8 6 A9 9 0 0 1 14.2 6 M4 8.6 A5.8 5.8 0 0 1 12 8.6 M6.2 11.1 A2.6 2.6 0 0 1 9.8 11.1 M8 13.4 V13.5"),
        ["bluetooth"] = G("M4.5 5 L11.5 11 L8 14 V2 L11.5 5 L4.5 11"),
        ["volume"] = G("M2 6 H5 L9 2.8 V13.2 L5 10 H2 Z M11.5 5.5 A3.5 3.5 0 0 1 11.5 10.5"),
        ["bell"] = G("M4 11.5 V7 A4 4 0 0 1 12 7 V11.5 L13.5 12.5 H2.5 Z M6.6 14 H9.4"),
        ["eye"] = G("M1.5 8 C3.2 4.8 5.4 3.5 8 3.5 C10.6 3.5 12.8 4.8 14.5 8 C12.8 11.2 10.6 12.5 8 12.5 C5.4 12.5 3.2 11.2 1.5 8 Z M8 6 A2 2 0 1 1 7.99 6 Z"),
        ["coffee"] = G("M2.5 6 H11.5 V10 A3.5 3.5 0 0 1 8 13.5 H6 A3.5 3.5 0 0 1 2.5 10 Z M11.5 7 H12.5 A1.5 1.5 0 0 1 12.5 10 H11.3 M5 2.5 V4 M8 2.5 V4"),
        ["gap"] = G("M2 2 H7 V14 H2 Z M9 2 H14 V14 H9 Z"),
        ["layers"] = G("M8 2 L14 5.2 L8 8.4 L2 5.2 Z M2 8.2 L8 11.4 L14 8.2 M2 11 L8 14.2 L14 11"),
    };

    static Geometry G(string data)
    {
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Icon), new FrameworkPropertyMetadata("circle", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(Icon), new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Icon), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Kind
    {
        get => (string)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public static bool Exists(string kind) => Shapes.ContainsKey(kind);

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (!Shapes.TryGetValue(Kind ?? "", out var shape)) shape = Shapes["circle"];
        var scale = Size / 16.0;
        var pen = new Pen(Foreground, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new ScaleTransform(scale, scale));
        // "play" is the one filled shape.
        dc.DrawGeometry(Kind == "play" ? Foreground : null, pen, shape);
        dc.Pop();
    }
}

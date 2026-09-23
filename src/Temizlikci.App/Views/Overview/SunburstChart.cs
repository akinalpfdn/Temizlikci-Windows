using System.ComponentModel;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Temizlikci.App.Theme;
using Temizlikci.Domain.Layout;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Overview;
using Temizlikci.Presentation.Strings;
using Windows.System;
using Windows.UI;

namespace Temizlikci.App.Views.Overview;

/// <summary>
/// The sunburst for the open folder, drawn with Win2D. Click selects, double-click (or Enter) opens a folder, clicking
/// the center (or Escape) goes up; arrow keys move between siblings and rings. Geometry and hit testing come from
/// <see cref="SunburstLayout"/>, so this class only draws and routes input.
/// </summary>
public sealed partial class SunburstChart : UserControl
{
    private const float SegmentGap = 2;
    private const float HatchSpacing = 6;
    private const float HatchWidth = 2;
    private const float LabelFontSize = 12;
    private const double LabelMinimumSweep = 0.42;
    private const double HoverDimming = 0.42;

    private readonly CanvasControl canvas = new();
    private readonly SunburstCenter center = new();
    private LocationScanModel? model;

    public SunburstChart()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        var grid = new Grid();
        grid.Children.Add(canvas);
        grid.Children.Add(center);
        Content = grid;
        canvas.Draw += OnDraw;
        canvas.PointerMoved += OnPointerMoved;
        canvas.PointerExited += (_, _) => SetHovered(null);
        canvas.Tapped += OnTapped;
        canvas.DoubleTapped += OnDoubleTapped;
        canvas.PointerPressed += (_, _) => Focus(FocusState.Pointer);
        KeyDown += OnKeyDown;
        ActualThemeChanged += (_, _) => canvas.Invalidate();
        SizeChanged += (_, _) => canvas.Invalidate();
        Unloaded += (_, _) =>
        {
            Model = null;
            // Win2D resources belong to the control; release them with it.
            canvas.RemoveFromVisualTree();
        };
    }

    public LocationScanModel? Model
    {
        get => model;
        set
        {
            if (model is not null) model.PropertyChanged -= OnModelChanged;
            model = value;
            if (model is not null) model.PropertyChanged += OnModelChanged;
            center.Model = model;
            Refresh();
        }
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        canvas.Invalidate();
        center.Refresh();
        if (model?.CurrentFolder is { } folder)
        {
            AutomationProperties.SetName(this, L10n.ChartAccessibilityLabel(model.Title(folder)));
        }
    }

    // MARK: Geometry

    private float Side => (float)Math.Min(canvas.ActualWidth, canvas.ActualHeight);

    /// <summary>The top-left corner of the chart's square inside the control.</summary>
    private Vector2 Origin => new((float)(canvas.ActualWidth - Side) / 2, (float)(canvas.ActualHeight - Side) / 2);

    private (double X, double Y) ChartPoint(Windows.Foundation.Point point) => (point.X - Origin.X, point.Y - Origin.Y);

    // MARK: Input

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (model is null) return;
        var (x, y) = ChartPoint(e.GetCurrentPoint(canvas).Position);
        SetHovered(SunburstLayout.SegmentAt(x, y, Side, model.Segments)?.Id);
        ProtectedCursor = model.HoveredId is not null || SunburstLayout.IsInHole(x, y, Side) && model.CanGoUp
            ? InputSystemCursor.Create(InputSystemCursorShape.Hand)
            : null;
    }

    private void SetHovered(string? id)
    {
        if (model is null || model.HoveredId == id) return;
        model.HoveredId = id;
        canvas.Invalidate();
        center.Refresh();
    }

    private void OnTapped(object sender, TappedRoutedEventArgs e)
    {
        if (model is null) return;
        var (x, y) = ChartPoint(e.GetPosition(canvas));
        if (SunburstLayout.IsInHole(x, y, Side))
        {
            model.GoUp();
            return;
        }
        model.SelectById(SunburstLayout.SegmentAt(x, y, Side, model.Segments)?.NodeId);
    }

    private void OnDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (model is null) return;
        var (x, y) = ChartPoint(e.GetPosition(canvas));
        if (SunburstLayout.SegmentAt(x, y, Side, model.Segments)?.NodeId is { } id && model.NodeById(id) is { } node) model.Open(node);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (model is null) return;
        e.Handled = true;
        switch (e.Key)
        {
            case VirtualKey.Left: model.SelectSibling(-1); break;
            case VirtualKey.Right: model.SelectSibling(1); break;
            case VirtualKey.Up: model.SelectParentRing(); break;
            case VirtualKey.Down: model.SelectChildRing(); break;
            case VirtualKey.Enter: model.OpenSelection(); break;
            case VirtualKey.Escape: model.GoUp(); break;
            default: e.Handled = false; break;
        }
    }

    // MARK: Drawing

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (model is null || Side <= 0) return;
        var session = args.DrawingSession;
        var appearance = ChartColors.Appearance(ActualTheme);
        var surface = ChartColors.Surface(appearance);
        var ink = ThemeColors.Get("CaptionForegroundColor", ActualTheme);
        float radius = Side / 2;
        var middle = Origin + new Vector2(radius, radius);
        var hovered = model.HoveredId is { } hoveredId ? model.Segments.FirstOrDefault(segment => segment.Id == hoveredId) : null;
        string? selectedId = model.Selection?.Id;

        foreach (var segment in model.Segments)
        {
            var (inner, outer) = SunburstLayout.RingBounds(segment.Depth);
            using var geometry = Sector(sender, middle, (float)(inner * radius), (float)(outer * radius), segment.StartAngle, segment.EndAngle);
            double opacity = hovered is not null && !SunburstLayout.IsWithin(segment, hovered) ? HoverDimming : 1;
            var fill = model.DisplayFill(segment);
            DrawFill(session, geometry, fill, appearance, opacity);
            if (fill.Role != FillRole.Pending) session.DrawGeometry(geometry, surface, SegmentGap);
            if (selectedId is not null && segment.NodeId == selectedId) session.DrawGeometry(geometry, ink, SegmentGap);
        }
        DrawLabels(session, sender, middle, radius, appearance);
    }

    private static void DrawFill(CanvasDrawingSession session, CanvasGeometry geometry, SegmentFill fill, Presentation.Theming.ChartAppearance appearance, double opacity)
    {
        if (ChartColors.For(fill, appearance) is { } color)
        {
            session.FillGeometry(geometry, ChartColors.WithOpacity(color, opacity));
            return;
        }
        if (fill.IsHatched)
        {
            session.FillGeometry(geometry, ChartColors.WithOpacity(ChartColors.Dimmed(appearance), opacity));
            var bounds = geometry.ComputeBounds();
            using (session.CreateLayer((float)opacity, geometry))
            {
                var hatch = ChartColors.Hatch(appearance);
                float height = (float)bounds.Height;
                for (float x = (float)bounds.X - height; x < bounds.X + bounds.Width; x += HatchSpacing)
                {
                    // Rising stripes for space no folder accounts for, falling ones for folders that couldn't be read.
                    var start = fill.Role == FillRole.UnattributedHatch ? new Vector2(x, (float)(bounds.Y + bounds.Height)) : new Vector2(x, (float)bounds.Y);
                    var end = fill.Role == FillRole.UnattributedHatch ? new Vector2(x + height, (float)bounds.Y) : new Vector2(x + height, (float)(bounds.Y + bounds.Height));
                    session.DrawLine(start, end, hatch, HatchWidth);
                }
            }
            return;
        }
        if (fill.Role == FillRole.Pending)
        {
            using var dashes = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
            session.DrawGeometry(geometry, ChartColors.WithOpacity(ChartColors.Hatch(appearance), opacity), 1, dashes);
        }
    }

    private void DrawLabels(CanvasDrawingSession session, ICanvasResourceCreator creator, Vector2 middle, float radius, Presentation.Theming.ChartAppearance appearance)
    {
        var (inner, outer) = SunburstLayout.RingBounds(1);
        float labelRadius = (float)((inner + outer) / 2 * radius);
        float thickness = (float)((outer - inner) * radius);
        using var nameFormat = new CanvasTextFormat { FontSize = LabelFontSize, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, WordWrapping = CanvasWordWrapping.NoWrap };
        using var sizeFormat = new CanvasTextFormat { FontSize = LabelFontSize, WordWrapping = CanvasWordWrapping.NoWrap };
        foreach (var segment in model!.Segments)
        {
            if (segment.Depth != 1 || segment.Sweep < LabelMinimumSweep || segment.IsMerged) continue;
            var fill = ChartColors.For(model.DisplayFill(segment), appearance);
            var ink = fill is { } color ? ChartColors.LabelInk(color) : ThemeColors.Get("CaptionForegroundColor", ActualTheme);
            float maxWidth = Math.Min(labelRadius * (float)segment.Sweep, thickness * 1.9f);
            using var size = new CanvasTextLayout(creator, Format.Bytes(segment.AllocatedSize), sizeFormat, float.MaxValue, float.MaxValue);
            if (size.LayoutBounds.Width > maxWidth) continue;
            double angle = segment.MidAngle;
            var point = middle + new Vector2((float)(labelRadius * Math.Sin(angle)), (float)(-labelRadius * Math.Cos(angle)));
            float height = (float)(size.LayoutBounds.Height * 2);
            // Labels are horizontal while the ring curves, so the whole box has to sit inside the slice, not just its middle.
            CanvasTextLayout? name = null;
            for (float width = maxWidth; width >= size.LayoutBounds.Width; width -= LabelFontSize)
            {
                name = FittedLabel(creator, model.Title(segment), nameFormat, width);
                float boxWidth = Math.Max((float)size.LayoutBounds.Width, (float)(name?.LayoutBounds.Width ?? 0));
                if (name is not null && BoxFits(point - middle, boxWidth, height, segment, inner * radius, outer * radius)) break;
                name?.Dispose();
                name = null;
            }
            if (name is null) continue;
            using var fitted = name;
            session.DrawTextLayout(name, point - new Vector2((float)name.LayoutBounds.Width / 2, (float)name.LayoutBounds.Height), ink);
            session.DrawTextLayout(size, point - new Vector2((float)size.LayoutBounds.Width / 2, 0), ink);
        }
    }

    /// <summary>Whether a box centered on <paramref name="center"/> (relative to the chart middle) lies inside a slice.</summary>
    private static bool BoxFits(Vector2 center, float width, float height, SunburstSegment segment, double inner, double outer)
    {
        const float Inset = 3;
        for (int corner = 0; corner < 4; corner++)
        {
            var point = center + new Vector2((corner % 2 == 0 ? -1 : 1) * (width / 2 + Inset), (corner < 2 ? -1 : 1) * (height / 2 + Inset));
            double distance = point.Length();
            if (distance < inner || distance > outer) return false;
            double angle = Math.Atan2(point.X, -point.Y);
            if (angle < 0) angle += 2 * Math.PI;
            if (angle < segment.StartAngle || angle > segment.EndAngle) return false;
        }
        return true;
    }

    /// <summary>The label, shortened with an ellipsis to fit, or <c>null</c> when not even one character fits.</summary>
    private static CanvasTextLayout? FittedLabel(ICanvasResourceCreator creator, string title, CanvasTextFormat format, float maxWidth)
    {
        for (int length = title.Length; length > 0; length--)
        {
            string text = length == title.Length ? title : title[..length] + "…";
            var layout = new CanvasTextLayout(creator, text, format, float.MaxValue, float.MaxValue);
            if (layout.LayoutBounds.Width <= maxWidth) return layout;
            layout.Dispose();
        }
        return null;
    }

    /// <summary>A ring slice between two radii. Angles are radians clockwise from 12 o'clock.</summary>
    private static CanvasGeometry Sector(ICanvasResourceCreator creator, Vector2 middle, float inner, float outer, double start, double end)
    {
        // A full ring can't be drawn as one arc: its start and end points coincide.
        if (end - start >= 2 * Math.PI - 1e-6)
        {
            using var outerCircle = CanvasGeometry.CreateEllipse(creator, middle, outer, outer);
            using var innerCircle = CanvasGeometry.CreateEllipse(creator, middle, inner, inner);
            return outerCircle.CombineWith(innerCircle, Matrix3x2.Identity, CanvasGeometryCombine.Exclude);
        }
        using var builder = new CanvasPathBuilder(creator);
        var arcSize = end - start > Math.PI ? CanvasArcSize.Large : CanvasArcSize.Small;
        builder.BeginFigure(Point(middle, outer, start));
        builder.AddArc(Point(middle, outer, end), outer, outer, 0, CanvasSweepDirection.Clockwise, arcSize);
        builder.AddLine(Point(middle, inner, end));
        builder.AddArc(Point(middle, inner, start), inner, inner, 0, CanvasSweepDirection.CounterClockwise, arcSize);
        builder.EndFigure(CanvasFigureLoop.Closed);
        return CanvasGeometry.CreatePath(builder);
    }

    private static Vector2 Point(Vector2 middle, float radius, double angle) =>
        middle + new Vector2((float)(radius * Math.Sin(angle)), (float)(-radius * Math.Cos(angle)));
}

/// <summary>The chart's center: the focused item's name, size and share, always visible as text.</summary>
public sealed partial class SunburstCenter : StackPanel
{
    private readonly TextBlock up = new() { Style = StyleFor("ChartCaptionTextStyle") };
    private readonly TextBlock name = new() { Style = StyleFor("ChartCenterNameTextStyle") };
    private readonly TextBlock size = new() { Style = StyleFor("ChartCenterValueTextStyle") };
    private readonly TextBlock caption = new() { Style = StyleFor("ChartCaptionTextStyle") };

    public SunburstCenter()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        IsHitTestVisible = false;
        Spacing = (double)Application.Current.Resources["SpacingXXSmall"];
        Children.Add(up);
        Children.Add(name);
        Children.Add(size);
        Children.Add(caption);
        SizeChanged += (_, _) => Refresh();
    }

    public LocationScanModel? Model { get; set; }

    private static Style StyleFor(string key) => (Style)Application.Current.Resources[key];

    public void Refresh()
    {
        if (Model is not { } model || model.FocusNode is not { } node)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        // The hole's diameter is HoleFraction of the side; text keeps a margin inside it.
        MaxWidth = Parent is FrameworkElement host ? Math.Min(host.ActualWidth, host.ActualHeight) * SunburstLayout.HoleFraction * 0.92 : double.PositiveInfinity;
        bool isFolder = model.CurrentFolder is { } folder && folder.Id == node.Id;
        var parent = model.Path.Count > 1 ? model.Path[^2] : (Domain.Tree.NodeRef?)null;
        up.Text = model.CanGoUp && isFolder && parent is { } above ? "↑ " + L10n.ChartGoUp(model.Title(above)) : string.Empty;
        up.Visibility = up.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        name.Text = model.Title(node);
        bool isRoot = model.Path.Count > 0 && node.Id == model.Path[0].Id;
        size.Text = Format.Bytes(model.IsScanning && isRoot ? model.Progress.AllocatedSize : node.Node.AllocatedSize);
        caption.Text = Caption(model, node, isRoot, isFolder) ?? string.Empty;
        caption.Visibility = caption.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string? Caption(LocationScanModel model, Domain.Tree.NodeRef node, bool isRoot, bool isFolder)
    {
        if (isRoot)
        {
            if (model.IsScanning) return L10n.ChartMeasuredSoFar;
            return model.Usage is { } usage ? L10n.ChartUsedOfVolume(Format.Bytes(usage.TotalCapacity)) : null;
        }
        var container = isFolder ? model.Parent(node) : model.CurrentFolder;
        if (container is not { } holder || LocationScanModel.Share(node.Node, holder.Node) is not { } share) return null;
        return L10n.ChartShareOf(Format.Percent(share), model.Title(holder));
    }
}

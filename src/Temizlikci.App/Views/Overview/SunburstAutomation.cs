using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Temizlikci.Domain.Layout;
using Temizlikci.Presentation.Formatting;
using Temizlikci.Presentation.Overview;
using Windows.Foundation;

namespace Temizlikci.App.Views.Overview;

/// <summary>
/// The chart as screen readers see it: a list whose items are the segments of the inner ring, each with its name, size
/// and share, where it is on screen, and Invoke to select it. The contents list next to the chart offers the same items
/// with full keyboard navigation; this makes the chart itself readable too.
/// </summary>
internal sealed partial class SunburstAutomationPeer(SunburstChart owner) : FrameworkElementAutomationPeer(owner)
{
    private readonly Dictionary<string, SegmentAutomationPeer> peers = [];
    private List<AutomationPeer> children = [];

    private SunburstChart Chart => (SunburstChart)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.List;

    protected override string GetClassNameCore() => nameof(SunburstChart);

    protected override IList<AutomationPeer> GetChildrenCore()
    {
        if (Chart.Model is not { } model) return [];
        var current = new List<AutomationPeer>();
        foreach (var segment in model.Segments.Where(segment => segment.Depth == 1))
        {
            if (!peers.TryGetValue(segment.Id, out var peer))
            {
                peer = new SegmentAutomationPeer(this);
                peers[segment.Id] = peer;
            }
            peer.Segment = segment;
            current.Add(peer);
        }
        children = current;
        return current;
    }

    /// <summary>Where a segment is on screen: its box inside the chart, placed and scaled like the chart itself.</summary>
    internal Rect ScreenBounds(SunburstSegment segment)
    {
        var chartOnScreen = GetBoundingRectangle();
        if (Chart.ActualWidth <= 0 || chartOnScreen.Width <= 0) return chartOnScreen;
        double scale = chartOnScreen.Width / Chart.ActualWidth;
        var local = Chart.LocalBounds(segment);
        return new Rect(chartOnScreen.X + local.X * scale, chartOnScreen.Y + local.Y * scale, local.Width * scale, local.Height * scale);
    }

    internal LocationScanModel? Model => Chart.Model;

    internal AutomationPeer? Sibling(SegmentAutomationPeer peer, int offset)
    {
        int index = children.IndexOf(peer);
        if (index < 0) return null;
        index += offset;
        return index >= 0 && index < children.Count ? children[index] : null;
    }
}

/// <summary>One segment of the inner ring, as a list item.</summary>
internal sealed partial class SegmentAutomationPeer(SunburstAutomationPeer chart) : AutomationPeer, IInvokeProvider
{
    public SunburstSegment? Segment { get; set; }

    public void Invoke()
    {
        if (Segment?.NodeId is { } id) chart.Model?.SelectById(id);
    }

    protected override string GetNameCore()
    {
        if (Segment is not { } segment || chart.Model is not { } model) return string.Empty;
        string size = Format.Bytes(segment.AllocatedSize);
        long total = model.CurrentFolder?.Node.AllocatedSize ?? 0;
        return total > 0 ? $"{model.Title(segment)}, {size}, {Format.Percent((double)segment.AllocatedSize / total)}" : $"{model.Title(segment)}, {size}";
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override string GetClassNameCore() => nameof(SunburstSegment);

    protected override Rect GetBoundingRectangleCore() => Segment is { } segment ? chart.ScreenBounds(segment) : default;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsEnabledCore() => true;

    protected override bool IsOffscreenCore() => false;

    protected override bool IsKeyboardFocusableCore() => false;

    protected override object? GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Invoke && Segment?.NodeId is not null ? this : null;

    protected override object? NavigateCore(AutomationNavigationDirection direction) => direction switch
    {
        AutomationNavigationDirection.Parent => chart,
        AutomationNavigationDirection.NextSibling => chart.Sibling(this, 1),
        AutomationNavigationDirection.PreviousSibling => chart.Sibling(this, -1),
        _ => null,
    };
}

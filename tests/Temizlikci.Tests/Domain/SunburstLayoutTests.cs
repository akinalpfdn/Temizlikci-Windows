using Temizlikci.Domain.Layout;
using Temizlikci.Domain.Tree;
using static Temizlikci.Tests.Support.TreeBuilder;

namespace Temizlikci.Tests.Domain;

public sealed class SunburstLayoutTests
{
    [Fact]
    public void Should_FillTheWholeFirstRingInProportion_When_LayingOutAFolder()
    {
        var segments = SunburstLayout.Segments(Sample(), RootPath);
        var ring = segments.Where(segment => segment.Depth == 1).ToList();

        Assert.Equal(2 * Math.PI, ring.Sum(segment => segment.Sweep), 9);
        Assert.Equal(["Apps", "Docs", "movie.mov"], ring.Select(segment => segment.Name));
        Assert.Equal(2 * Math.PI * 0.6, ring[0].Sweep, 9);
    }

    [Fact]
    public void Should_NestChildrenInsideTheirParentsAngle_When_DrawingAtMostThreeRings()
    {
        var deep = Root(Dir("a", Dir("b", Dir("c", Dir("d", File("x", 10))))));
        Assert.Equal(SunburstLayout.RingCount, SunburstLayout.Segments(deep, RootPath).Max(segment => segment.Depth));

        var sample = SunburstLayout.Segments(Sample(), RootPath);
        var apps = sample.Single(segment => segment.Name == "Apps" && segment.Depth == 1);
        foreach (var child in sample.Where(segment => segment.Depth == 2 && segment.NodeId!.Contains(@"\Apps\", StringComparison.Ordinal)))
        {
            Assert.True(child.StartAngle >= apps.StartAngle - 1e-9 && child.EndAngle <= apps.EndAngle + 1e-9);
        }
    }

    [Fact]
    public void Should_MergeSliversIntoOneNeutralSegment_When_TheyAreTooThinToClick()
    {
        var tiny = Enumerable.Range(0, 10).Select(index => File($"t{index}", 1));
        var root = Root([File("big", 10_000), .. tiny]);

        var segments = SunburstLayout.Segments(root, RootPath);

        var merged = segments.Where(segment => segment.IsMerged).ToList();
        Assert.Single(merged);
        Assert.Equal(10, merged[0].AllocatedSize);
        Assert.Equal(SegmentFill.Neutral, merged[0].Fill);
        Assert.All(segments.Where(segment => !segment.IsMerged), segment => Assert.True(segment.Sweep >= SunburstLayout.MinimumSweep));
    }

    [Fact]
    public void Should_GiveSlotsToTheEightLargestItems_When_KeepingSpecialSpaceNeutralOrHatched()
    {
        var children = Enumerable.Range(0, 9).Select(index => File($"f{index}", 100 - index)).ToList();
        children.Add(FileNode.Unattributed(500));
        children.Add(FileNode.Inaccessible("Locked"));
        var segments = SunburstLayout.Segments(Root([.. children]), RootPath);

        Assert.Equal(SegmentFill.ForSlot(0, 1), segments.Single(s => s.Name == "f0").Fill);
        Assert.Equal(SegmentFill.ForSlot(7, 1), segments.Single(s => s.Name == "f7").Fill);
        Assert.Equal(SegmentFill.Neutral, segments.Single(s => s.Name == "f8").Fill);
        Assert.Equal(SegmentFill.UnattributedHatch, segments.Single(s => s.NodeId!.EndsWith("unattributed", StringComparison.Ordinal)).Fill);
    }

    [Fact]
    public void Should_PassAFoldersSlotToItsDescendants_When_ColoringDeeperRings()
    {
        var segments = SunburstLayout.Segments(Sample(), RootPath);

        Assert.Equal(SegmentFill.ForSlot(1, 2), segments.Single(s => s.Name == "Reports").Fill);
        Assert.Equal(SegmentFill.ForSlot(1, 3), segments.Single(s => s.Name == "q1.pdf").Fill);
    }

    [Fact]
    public void Should_FindTheSegmentUnderAPoint_When_TheCenterIsTheHole()
    {
        var segments = SunburstLayout.Segments(Sample(), RootPath);
        const double side = 400;
        var (inner, outer) = SunburstLayout.RingBounds(1);
        double ringOneMiddle = (inner + outer) / 2 * 200;

        Assert.Equal("Apps", SunburstLayout.SegmentAt(201, 200 - ringOneMiddle, side, segments)?.Name);
        Assert.Null(SunburstLayout.SegmentAt(200, 200, side, segments));
        Assert.True(SunburstLayout.IsInHole(200, 200, side));
        Assert.Null(SunburstLayout.SegmentAt(0, 0, side, segments));
    }

    [Fact]
    public void Should_TreatOuterDescendants_When_CheckingWhetherASegmentIsWithinAnother()
    {
        var segments = SunburstLayout.Segments(Sample(), RootPath);
        var apps = segments.Single(s => s.Name == "Apps");
        var big = segments.Single(s => s.Name == "Big.app");
        var docs = segments.Single(s => s.Name == "Docs");

        Assert.True(SunburstLayout.IsWithin(big, apps));
        Assert.False(SunburstLayout.IsWithin(big, docs));
        Assert.False(SunburstLayout.IsWithin(apps, big));
    }
}

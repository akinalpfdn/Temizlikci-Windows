using Temizlikci.Domain.Tree;
using Temizlikci.Tests.Support;
using static Temizlikci.Tests.Support.TreeBuilder;

namespace Temizlikci.Tests.Domain;

public sealed class TreeTests
{
    [Fact]
    public void Should_SortChildrenAndTotalSizes_When_BuildingADirectory()
    {
        var tree = Sample();

        Assert.Equal(1000, tree.AllocatedSize);
        Assert.Equal(5, tree.FileCount);
        Assert.Equal(["Apps", "Docs", "movie.mov"], tree.Children.Select(child => child.Name));
    }

    [Fact]
    public void Should_UseTheFolderPathWithASuffix_When_AnAggregateNeedsAnId()
    {
        var smaller = FileNode.SmallerFiles(3, 30);

        Assert.Equal(@"C:\Scan\Apps", FileNode.ChildPath(@"C:\Scan\Apps", smaller));
        Assert.Equal("C:\\Scan\\Apps\0smaller-files", FileNode.ChildId(@"C:\Scan\Apps", smaller));
        Assert.Equal(@"C:\Scan\Apps\Big.app", FileNode.ChildId(@"C:\Scan\Apps", File("Big.app", 1)));
    }

    [Fact]
    public void Should_RemoveANestedItemAndReTotalEveryFolderAboveIt_When_Editing()
    {
        var tree = Sample();

        var updated = tree.RemovingDescendant([RootPath, Id("Docs"), Id(@"Docs\Reports")]);

        Assert.NotNull(updated);
        Assert.Equal(800, updated.AllocatedSize);
        var docs = updated.Children.Single(child => child.Name == "Docs");
        Assert.Equal(100, docs.AllocatedSize);
        Assert.Equal(["notes.txt"], docs.Children.Select(child => child.Name));
    }

    [Fact]
    public void Should_PutARemovedItemBack_When_InsertingIt()
    {
        var tree = Sample();
        var reports = tree.Children.Single(c => c.Name == "Docs").Children.Single(c => c.Name == "Reports");
        var removed = tree.RemovingDescendant([RootPath, Id("Docs"), Id(@"Docs\Reports")])!;

        var restored = removed.InsertingDescendant(reports, [RootPath, Id("Docs")]);

        Assert.NotNull(restored);
        Assert.Equal(tree.AllocatedSize, restored.AllocatedSize);
        Assert.Equal(["Reports", "notes.txt"], restored.Children.Single(c => c.Name == "Docs").Children.Select(c => c.Name));
    }

    [Fact]
    public void Should_RefuseEdits_When_ThePathDoesNotExist()
    {
        var tree = Sample();

        Assert.Null(tree.RemovingDescendant([RootPath, @"C:\Scan\nope"]));
        Assert.Null(tree.InsertingDescendant(File("x", 1), [RootPath, @"C:\Scan\nope"]));
        Assert.Null(tree.RemovingDescendant([@"D:\Other", Id("Docs")]));
    }

    [Fact]
    public void Should_StopAtTheFirstMissingId_When_FollowingIdsDownTheTree()
    {
        var tree = Sample();

        var found = tree.NodesAlong([RootPath, Id("Docs"), Id(@"Docs\Missing")]);

        Assert.Equal([RootPath, "Docs"], found.Select(item => item.Node.Name));
        Assert.Equal(@"C:\Scan\Docs", found[1].Path);
    }

    [Fact]
    public void Should_FindTheChainToAnItem_When_LocatingItByPath()
    {
        var chain = Sample().Locate(@"c:\scan\docs\reports\Q1.PDF");

        Assert.NotNull(chain);
        Assert.Equal([RootPath, "Docs", "Reports", "q1.pdf"], chain.Select(item => item.Node.Name));
    }

    [Fact]
    public void Should_NotStepIntoSmallerFiles_When_TheyShareTheFolderPath()
    {
        // "Smaller files" is the largest child here and shares the root's path; locating must skip it.
        var tree = Root(FileNode.SmallerFiles(50, 5000), Dir("Docs", File("a.txt", 20_000_000)));

        var chain = tree.Locate(@"C:\Scan\Docs\a.txt");

        Assert.NotNull(chain);
        Assert.Equal("a.txt", chain[^1].Node.Name);
    }

    [Theory]
    [InlineData(@"C:\Users\dev\", @"C:\Users\dev")]
    [InlineData("C:", @"C:\")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData("C:/Users/dev/", @"C:\Users\dev")]
    public void Should_NormalizeSeparators_When_TrimmingAPath(string input, string expected)
    {
        Assert.Equal(expected, NodePath.Trim(input));
    }

    [Theory]
    [InlineData(@"C:\Users\dev", @"C:\Users")]
    [InlineData(@"C:\Users", @"C:\")]
    [InlineData(@"C:\", null)]
    public void Should_FindTheContainingFolder_When_AskedForAParent(string path, string? expected)
    {
        Assert.Equal(expected, NodePath.Parent(path));
    }

    [Fact]
    public void Should_IgnoreCaseAndRequireASeparator_When_CheckingContainment()
    {
        Assert.True(NodePath.IsWithin(@"C:\users\DEV\x", @"C:\Users\dev"));
        Assert.False(NodePath.IsWithin(@"C:\Users\developer", @"C:\Users\dev"));
        Assert.False(NodePath.IsWithin(@"C:\Users\dev", @"C:\Users\dev"));
        Assert.True(NodePath.IsWithin(@"C:\Windows", @"C:\"));
    }
}

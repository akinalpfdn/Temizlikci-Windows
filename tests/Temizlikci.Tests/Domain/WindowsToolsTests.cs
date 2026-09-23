using System.Text;
using Temizlikci.Domain.Tools;

namespace Temizlikci.Tests.Domain;

public sealed class WindowsToolsTests
{
    [Fact]
    public void Should_ReadNamesFromUtf16Output_When_ItHasAMarkAndNuls()
    {
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("Ubuntu\r\nDebian\r\n\0")];

        var names = WslOutput.Names(Encoding.Unicode.GetString(bytes), ["Ubuntu", "Debian", "Alpine"]);

        Assert.Equal(["Debian", "Ubuntu"], names.Order());
    }

    [Fact]
    public void Should_IgnoreLinesThatAreNotDistributions_When_WslPrintsAMessage()
    {
        Assert.Empty(WslOutput.Names("There are no running distributions.\r\n", ["Ubuntu"]));
    }

    [Theory]
    [InlineData(@"\\?\C:\WSL\Ubuntu", @"C:\WSL\Ubuntu")]
    [InlineData(@"\\?\UNC\server\share\wsl", @"\\server\share\wsl")]
    [InlineData(@"D:\WSL", @"D:\WSL")]
    public void Should_DropTheLongPathPrefix_When_ShowingARegistryPath(string stored, string expected)
    {
        Assert.Equal(expected, WslOutput.PlainPath(stored));
    }

    [Fact]
    public void Should_RefuseAPathThatCouldBreakTheScript_When_ItHasAQuote()
    {
        Assert.Throws<ArgumentException>(() => WslOutput.CompactScript("C:\\a\"\r\nclean\r\n.vhdx"));
    }

    [Theory]
    [InlineData("Ubuntu", 2, true, true)]
    [InlineData("Legacy", 1, false, true)]
    [InlineData("docker-desktop", 2, false, false)]
    [InlineData("docker-desktop-data", 2, false, false)]
    public void Should_OfferOnlySafeActions_When_ADistributionIsDockersOrWsl1(string name, int version, bool compact, bool remove)
    {
        var distribution = new WslDistribution(new WslRegistration(name, @"C:\WSL\" + name, version, "ext4.vhdx", false), false, null);

        Assert.Equal(compact, distribution.CanCompact);
        Assert.Equal(remove, distribution.CanUnregister);
    }
}

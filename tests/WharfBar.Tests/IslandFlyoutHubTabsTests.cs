using WharfBar.Services;
using WharfBar.Views;
using Xunit;

namespace WharfBar.Tests;

/// <summary>
/// Şelf sekmesi hub'a bağlanırken tag→gövde eşleşmesi pure yardımcı üzerinden sınanır:
/// <c>IslandFlyout.TabIdForBody</c> her sekme için gövde Tag'indeki kimliği üretir.
/// </summary>
public class IslandFlyoutHubTabsTests
{
    [Fact]
    public void TabIdForBody_ShelfId_MatchesTag()
    {
        Assert.Equal("shelf", IslandFlyout.TabIdForBody(IslandHubTab.Shelf));
    }
}

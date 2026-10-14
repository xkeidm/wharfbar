using System.Text.Json;
using WharfBar.Models;
using WharfBar.Services;
using Xunit;

namespace WharfBar.Tests;

public class TrayOverflowMembershipTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void EmptyBox_AllKnownKeysStayVisible()
    {
        string[] known = ["OneNote", "Discord", "Steam"];

        Assert.Equal(known, TrayOverflowMembership.VisibleKeys(known, null));
        Assert.Equal(known, TrayOverflowMembership.VisibleKeys(known, []));
        Assert.Empty(TrayOverflowMembership.BoxedKeys(known, null));
        Assert.Empty(TrayOverflowMembership.BoxedKeys(known, []));
        Assert.False(TrayOverflowMembership.IsInBox(null, "Discord"));
    }

    [Fact]
    public void UnknownKey_DefaultsVisible()
    {
        IReadOnlyList<string> box = ["OneNote"];

        Assert.False(TrayOverflowMembership.IsInBox(box, "Discord"));
        Assert.Equal(["Discord", "Steam"], TrayOverflowMembership.VisibleKeys(["Discord", "Steam", "OneNote"], box));
        Assert.Equal(["OneNote"], TrayOverflowMembership.BoxedKeys(["Discord", "OneNote"], box));
    }

    [Fact]
    public void MoveIn_ParksKey_AndKeepsExistingOrder()
    {
        List<string> parked = TrayOverflowMembership.MoveIn(["OneNote"], "Discord");

        Assert.Equal(["OneNote", "Discord"], parked);
        Assert.True(TrayOverflowMembership.IsInBox(parked, "Discord"));
        Assert.Equal(["Steam"], TrayOverflowMembership.VisibleKeys(["Steam", "Discord", "OneNote"], parked));
        Assert.Equal(["OneNote", "Discord"], TrayOverflowMembership.BoxedKeys(["Steam", "Discord", "OneNote"], parked));
    }

    [Fact]
    public void MoveIn_IsIdempotent()
    {
        List<string> once = TrayOverflowMembership.MoveIn(["OneNote"], "OneNote");
        List<string> twice = TrayOverflowMembership.MoveIn(once, "  OneNote  ");

        Assert.Equal(["OneNote"], once);
        Assert.Equal(["OneNote"], twice);
    }

    [Fact]
    public void MoveOut_RestoresVisibility()
    {
        List<string> parked = TrayOverflowMembership.MoveOut(["OneNote", "Discord", "Steam"], "Discord");

        Assert.Equal(["OneNote", "Steam"], parked);
        Assert.False(TrayOverflowMembership.IsInBox(parked, "Discord"));
        Assert.Equal(["Discord"], TrayOverflowMembership.VisibleKeys(["Discord", "OneNote", "Steam"], parked));
        Assert.Equal(["OneNote", "Steam"], TrayOverflowMembership.BoxedKeys(["Discord", "OneNote", "Steam"], parked));
    }

    [Fact]
    public void MoveOut_UnknownKey_LeavesBoxUnchanged()
    {
        List<string> parked = TrayOverflowMembership.MoveOut(["OneNote"], "Discord");

        Assert.Equal(["OneNote"], parked);
        Assert.False(TrayOverflowMembership.IsInBox(parked, "Discord"));
        Assert.Equal(["Discord"], TrayOverflowMembership.VisibleKeys(["Discord"], parked));
    }

    [Fact]
    public void Normalize_DropsBlanksAndDuplicates_PreservesOrder()
    {
        List<string?> raw = [" OneNote ", "", "Discord", "OneNote", "  ", null, "Steam", "Discord"];
        IReadOnlyList<string> normalized = TrayOverflowMembership.Normalize(raw);

        Assert.Equal(["OneNote", "Discord", "Steam"], normalized);
    }

    [Fact]
    public void AbsentIcon_StaysInPersistedBox_UntilItReturns()
    {
        IReadOnlyList<string> box = TrayOverflowMembership.Normalize(["OneNote", "OldApp"]);

        Assert.Equal(["OneNote", "OldApp"], box);
        Assert.Equal(["OneNote"], TrayOverflowMembership.BoxedKeys(["Discord", "OneNote"], box));
        Assert.DoesNotContain("OldApp", TrayOverflowMembership.VisibleKeys(["Discord", "OneNote"], box));
    }

    [Fact]
    public void KeysAreOrdinal_AndBlankMoveIsIgnored()
    {
        Assert.False(TrayOverflowMembership.IsInBox(["Wifi"], "wifi"));
        Assert.Equal(["Wifi"], TrayOverflowMembership.MoveIn(["Wifi"], "   "));
        Assert.Equal(["Wifi"], TrayOverflowMembership.MoveOut(["Wifi"], null));
        Assert.Empty(TrayOverflowMembership.VisibleKeys(null, ["Wifi"]));
    }

    [Fact]
    public void DragThreshold_RequiresMinimumMovement()
    {
        Assert.False(TrayOverflowMembership.ExceedsDragThreshold(3, 3, 4, 4));
        Assert.False(TrayOverflowMembership.ExceedsDragThreshold(0, 0, 4, 4));
        Assert.True(TrayOverflowMembership.ExceedsDragThreshold(4, 0, 4, 4));
        Assert.True(TrayOverflowMembership.ExceedsDragThreshold(0, -4, 4, 4));
        Assert.False(TrayOverflowMembership.ExceedsDragThreshold(double.NaN, 20, 4, 4));
    }

    [Fact]
    public void ConfigRoundTrip_KeepsOverflowKeysAndStatusOrder()
    {
        var config = new AppConfig
        {
            TopBar = new TopBarConfig
            {
                StatusItems = ["dateTime", "battery"],
                TrayOverflowIcons = ["OneNote"],
            },
        };

        string json = JsonSerializer.Serialize(config, JsonOptions);
        Assert.Contains("\"trayOverflowIcons\"", json, StringComparison.Ordinal);
        Assert.Contains("\"statusItems\"", json, StringComparison.Ordinal);

        AppConfig? loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
        Assert.NotNull(loaded);
        Assert.Equal(["OneNote"], loaded.TopBar.TrayOverflowIcons);
        Assert.Equal(["dateTime", "battery"], loaded.TopBar.StatusItems);
    }

    [Fact]
    public void MissingOverflowList_DeserializesEmpty_NewIconStaysVisible()
    {
        const string json = """{"schemaVersion":2,"topBar":{"statusItems":["dateTime","network"]}}""";

        AppConfig? loaded = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);

        Assert.NotNull(loaded);
        Assert.NotNull(loaded.TopBar.TrayOverflowIcons);
        Assert.Empty(loaded.TopBar.TrayOverflowIcons);
        Assert.Equal(["dateTime", "network"], loaded.TopBar.StatusItems);
        Assert.Equal(["Discord"], TrayOverflowMembership.VisibleKeys(["Discord"], loaded.TopBar.TrayOverflowIcons));
    }
}

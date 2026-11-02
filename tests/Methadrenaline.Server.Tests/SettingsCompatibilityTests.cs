using SecretAPI.Features.UserSettings;
using Xunit;

namespace Methadrenaline.Server.Tests;

public sealed class SettingsCompatibilityTests
{
    private sealed record Setting(int? Id, string Label);

    [Fact]
    public void ReassigningAnExistingSettingsArrayDoesNotMakeItsOwnIdsDuplicates()
    {
        var settings = new[] { new Setting(20070923, "See Perks"), new Setting(20070924, "See Upgrades") };
        var duplicate = new List<Setting>();
        Assert.Equal(settings, SettingBatchPolicy.Filter(settings, s => s.Id, Array.Empty<int>(), duplicate.Add));
        Assert.Equal(settings, SettingBatchPolicy.Filter(settings, s => s.Id, Array.Empty<int>(), duplicate.Add));
        Assert.Empty(duplicate);
    }

    [Fact]
    public void RealIdCollisionsAreExcludedWhileGroupHeadersRemainAvailable()
    {
        var header = new Setting(null, "Arcade");
        var valid = new Setting(20070923, "See Perks");
        var repeated = new Setting(20070923, "Duplicate");
        var reserved = new Setting(99, "Another plugin");
        var duplicate = new List<Setting>();
        var result = SettingBatchPolicy.Filter(new[] { header, valid, repeated, reserved, header }, s => s.Id, new[] { 99 }, duplicate.Add);
        Assert.Equal(new[] { header, valid, header }, result);
        Assert.Equal(new[] { repeated, reserved }, duplicate);
    }
}

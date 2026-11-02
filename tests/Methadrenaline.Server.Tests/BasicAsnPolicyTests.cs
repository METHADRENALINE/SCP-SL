using Methadrenaline.Server.Core;
using Xunit;

public class BasicAsnPolicyTests
{
    private const string Id = "123@discord";
    private const string Basic = "The ASN of the Internet Service Provider you are connecting from is globally blocked (basic level ban) - ASN8881 - 7A/example";
    [Fact] public void NamedDiscordAccountAllowsOnlyBasicNetworkBan()
        => Assert.True(BasicAsnPolicy.Allows(Id, 8881, Basic, new[] { Id }));
    [Fact] public void SaltedIdentityMatchesExactAccount()
        => Assert.True(BasicAsnPolicy.Allows(Id + "$salt", 8881, Basic, new[] { Id }));
    [Theory]
    [InlineData("Other ban")]
    [InlineData("You are globally banned for cheating.")]
    [InlineData("The ASN of the Internet Service Provider you are connecting from is globally blocked (full ban) - ASN8881 - 7A/example")]
    public void OtherReasonsRemainBlocked(string reason)
        => Assert.False(BasicAsnPolicy.Allows(Id, 8881, reason, new[] { Id }));
    [Fact] public void OtherAccountsRemainBlocked()
        => Assert.False(BasicAsnPolicy.Allows("124@discord", 8881, Basic, new[] { Id }));
    [Fact] public void EmptyAllowlistDenies()
        => Assert.False(BasicAsnPolicy.Allows(Id, 8881, Basic, System.Array.Empty<string>()));
    [Fact] public void MismatchedAsnDenies()
        => Assert.False(BasicAsnPolicy.Allows(Id, 123, Basic, new[] { Id }));
    [Fact] public void SteamAccountIsNotCovered()
        => Assert.False(BasicAsnPolicy.Allows("123@steam", 8881, Basic, new[] { "123@steam" }));
}

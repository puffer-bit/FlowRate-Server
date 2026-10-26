using Server.Core.Models.User;

namespace Server.Core.Tests.Models;

public class UserPasskeyTests
{
    private static UserPasskey Create(string? label = null) =>
        UserPasskey.Create(1, [1, 2], [3, 4], 0, label, Guid.Empty, DateTime.UtcNow);

    [Fact]
    public void New_IsEnabledAndNeverUsed()
    {
        var passkey = Create("YubiKey");

        Assert.True(passkey.IsEnabled);
        Assert.Null(passkey.LastUsedAt);
        Assert.Equal("YubiKey", passkey.Label);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("bad\u0007label")]
    public void Label_ThatCannotBeShown_IsTheDefault(string? label)
    {
        Assert.Equal(UserPasskey.DefaultLabel, Create(label).Label);
    }

    [Fact]
    public void Label_IsCut_ToItsLength()
    {
        Assert.Equal(UserPasskey.LabelMaxLength, Create(new string('a', 100)).Label.Length);
    }

    [Fact]
    public void Use_RecordsTheCounterAndTheTime()
    {
        var passkey = Create();
        var at = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        passkey.RecordUse(5, at);

        Assert.Equal(5u, passkey.SignatureCounter);
        Assert.Equal(at, passkey.LastUsedAt);
    }

    [Fact]
    public void Passkey_NeedsItsCredentialAndKey()
    {
        Assert.Throws<ArgumentException>(() => UserPasskey.Create(1, [], [3], 0, null, Guid.Empty, DateTime.UtcNow));
        Assert.Throws<ArgumentException>(() => UserPasskey.Create(1, [1], [], 0, null, Guid.Empty, DateTime.UtcNow));
    }
}

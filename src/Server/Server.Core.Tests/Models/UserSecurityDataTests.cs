using Server.Core.Models.User;

namespace Server.Core.Tests.Models;

public class UserSecurityDataTests
{
    [Fact]
    public void Empty_NeedsNoSecondStage()
    {
        var data = UserSecurityData.CreateEmpty(1);

        Assert.False(data.RequiresSecondStage);
        Assert.False(data.HasKnowledgeFactor);
    }

    [Fact]
    public void Password_IsAKnowledgeFactor_UntilRemoved()
    {
        var data = UserSecurityData.CreateEmpty(1);

        data.SetPassword("hash");
        Assert.True(data.IsPasswordEnabled);
        Assert.True(data.RequiresSecondStage);

        data.RemovePassword();
        Assert.False(data.IsPasswordEnabled);
        Assert.Null(data.PasswordHash);
    }

    [Fact]
    public void OfferedSecret_IsPending_UntilEnabled()
    {
        var data = UserSecurityData.CreateEmpty(1);

        data.OfferTwoFactorKey("SECRET");
        Assert.True(data.IsTwoFactorPending);
        Assert.False(data.RequiresSecondStage);

        data.EnableTwoFactor();
        Assert.False(data.IsTwoFactorPending);
        Assert.True(data.IsTwoFactorEnabled);
        Assert.True(data.RequiresSecondStage);
    }

    [Fact]
    public void EnabledSecret_IsNotReplacedByAnOffer()
    {
        var data = UserSecurityData.CreateEmpty(1);
        data.OfferTwoFactorKey("SECRET");
        data.EnableTwoFactor();

        Assert.Throws<InvalidOperationException>(() => data.OfferTwoFactorKey("OTHER"));
        Assert.Equal("SECRET", data.TwoFactorKey);
    }

    [Fact]
    public void Enabling_WithoutAnOffer_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => UserSecurityData.CreateEmpty(1).EnableTwoFactor());
    }

    [Fact]
    public void Disabling_ForgetsTheSecret()
    {
        var data = UserSecurityData.CreateEmpty(1);
        data.OfferTwoFactorKey("SECRET");
        data.EnableTwoFactor();

        data.DisableTwoFactor();

        Assert.False(data.IsTwoFactorEnabled);
        Assert.Null(data.TwoFactorKey);
    }

    [Fact]
    public void FlagsWithoutTheirSecrets_DoNotCount()
    {
        // As in rows written before the secrets were kept consistently
        var data = new UserSecurityData(1, null, null, isTwoFactorEnabled: true, isPasswordEnabled: true, false);

        Assert.False(data.IsTwoFactorEnabled);
        Assert.False(data.IsPasswordEnabled);
        Assert.False(data.RequiresSecondStage);
    }

    [Fact]
    public void Passkeys_AloneNeedASecondStage_ButNoKnowledgeFactor()
    {
        var data = UserSecurityData.CreateEmpty(1);

        data.SetPasskeysEnabled(true);

        Assert.True(data.RequiresSecondStage);
        Assert.False(data.HasKnowledgeFactor);
    }
}

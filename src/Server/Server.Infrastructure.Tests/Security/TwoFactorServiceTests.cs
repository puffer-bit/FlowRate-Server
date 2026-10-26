using Xunit;
using OtpNet;
using Server.Infrastructure.Security;

namespace Server.Infrastructure.Tests.Security;

public class TwoFactorServiceTests
{
    private readonly TwoFactorService _service = new();

    [Fact]
    public void CodeOfTheSecret_IsValid_AndTellsItsStep()
    {
        var secret = _service.GenerateSecretKey();
        var totp = new Totp(Base32Encoding.ToBytes(secret));

        Assert.True(_service.ValidateCode(secret, totp.ComputeTotp(), out var step));
        Assert.True(step > 0);
    }

    [Fact]
    public void CodeOfTheStepBefore_IsStillValid()
    {
        var secret = _service.GenerateSecretKey();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(DateTime.UtcNow.AddSeconds(-30));

        Assert.True(_service.ValidateCode(secret, code, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abcdef")]
    [InlineData("12345")]
    public void Garbage_IsNotValid(string code)
    {
        Assert.False(_service.ValidateCode(_service.GenerateSecretKey(), code, out _));
    }

    [Fact]
    public void CodeFromAnOldStep_IsNotValid()
    {
        var secret = _service.GenerateSecretKey();
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp(DateTime.UtcNow.AddMinutes(-5));

        Assert.False(_service.ValidateCode(secret, code, out _));
    }

    [Fact]
    public void BrokenSecret_IsNotValid()
    {
        Assert.False(_service.ValidateCode("not base32 !!", "123456", out _));
    }

    [Fact]
    public void QrCodeUri_NamesTheIssuerAndTheAccount()
    {
        var uri = _service.GetQrCodeUri("alice", "My Server", "ABCDEF");

        Assert.Equal("otpauth://totp/My%20Server:alice?secret=ABCDEF&issuer=My%20Server&digits=6&period=30", uri);
    }
}

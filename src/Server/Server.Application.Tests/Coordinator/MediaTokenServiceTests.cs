using Server.Application.Coordinator.Media;

namespace Server.Application.Tests.Coordinator;

public class MediaTokenServiceTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly Clock _clock = new();
    private readonly MediaTokenService _tokens;

    public MediaTokenServiceTests()
    {
        _tokens = new MediaTokenService(TimeSpan.FromMinutes(10), _clock);
    }

    [Fact]
    public void IssuedToken_NamesItsUser()
    {
        var (token, expiresAt) = _tokens.Issue(42);

        Assert.True(_tokens.TryValidate(token, out var userId));
        Assert.Equal(42, userId);
        Assert.Equal(_clock.Now.AddMinutes(10).UtcDateTime, expiresAt);
    }

    [Fact]
    public void ExpiredToken_IsRefused()
    {
        var (token, _) = _tokens.Issue(42);
        _clock.Now = _clock.Now.AddMinutes(10);

        Assert.False(_tokens.TryValidate(token, out _));
    }

    [Fact]
    public void TokenOfAnotherUser_CannotBeForged()
    {
        var (token, _) = _tokens.Issue(42);
        var parts = token.Split('.');

        Assert.False(_tokens.TryValidate($"43.{parts[1]}.{parts[2]}", out _));
        Assert.False(_tokens.TryValidate($"{parts[0]}.{long.Parse(parts[1]) + 1000}.{parts[2]}", out _));
    }

    [Fact]
    public void TokenOfAnotherService_IsRefused()
    {
        var (token, _) = new MediaTokenService(TimeSpan.FromMinutes(10), _clock).Issue(42);

        Assert.False(_tokens.TryValidate(token, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("42")]
    [InlineData("a.b.c")]
    [InlineData("42.99999999999.")]
    public void Garbage_IsRefused(string? token)
    {
        Assert.False(_tokens.TryValidate(token, out _));
    }

    [Theory]
    [InlineData("image/jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, true)]
    [InlineData("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, true)]
    [InlineData("image/gif", new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, true)]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, true)]
    [InlineData("image/png", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, false)]
    [InlineData("image/webp", new byte[] { 0x52, 0x49, 0x46, 0x46 }, false)]
    [InlineData("text/html", new byte[] { 0x3C, 0x68, 0x74, 0x6D }, false)]
    public void Signature_MustMatchTheDeclaredType(string mimeType, byte[] header, bool expected)
    {
        Assert.Equal(expected, MediaSignatures.Matches(mimeType, header));
    }
}

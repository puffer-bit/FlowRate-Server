using System.Text;
using Server.Application.Coordinator.Push;

namespace Server.Application.Tests.Coordinator;

public class PushCipherTests
{
    private const string ClientTestVector =
        "ZGVmZ2hpamtsbW5vMzm9CRabMvdQAyuHqCwO33jgZWXkHpcbybDYJ4mBiWr3gSG0BXa5PsrfWlyVI76Bejb9xRpoJk2UlN63g6OJqGXpqH23LF5M703XdyOmE5UBFntR1Yzp5OXAGuYIdiA16wPcHwYomkSRm7KswWiaxABf5We1W4MWYsZCMQoM2HXiKkkqScLds0lreifo9Cjj9a+QIz4/pKgBvVw+hX/RrvlcQn6q7dbZWmCZxdxEk/6mwbAV9bTh0U81+8ADOWyusev0";

    private static readonly byte[] Key = Enumerable.Range(0, PushCipher.KeyLength).Select(i => (byte)i).ToArray();

    [Fact]
    public void Decrypt_OpensTheVectorTheClientTestsUse()
    {
        var plaintext = Encoding.UTF8.GetString(PushCipher.Decrypt(Key, ClientTestVector));

        Assert.StartsWith("{\"coordinatorId\":\"coordinator\",\"chatId\":7,", plaintext);
        Assert.Contains("привет 😀", plaintext);
    }

    [Fact]
    public void Encrypt_UsesAFreshNonceAndRoundTrips()
    {
        var plaintext = "hello"u8.ToArray();

        var first = PushCipher.Encrypt(Key, plaintext);
        var second = PushCipher.Encrypt(Key, plaintext);

        Assert.NotEqual(first, second);
        Assert.Equal(plaintext, PushCipher.Decrypt(Key, first));
        Assert.Equal(PushCipher.NonceLength + plaintext.Length + PushCipher.TagLength,
            Convert.FromBase64String(first).Length);
    }
}

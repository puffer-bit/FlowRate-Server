using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Shared.Validation;

namespace Server.Application.Coordinator.Push;

public static class PushReplyToken
{
    public static string Create() =>
        Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(Limits.PushReplyTokenLength));

    public static byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));
}

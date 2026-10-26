using System.Buffers.Binary;
using System.Buffers.Text;
using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Server.Infrastructure.Tests.Security;

/// <summary>
/// A FIDO2 authenticator in memory with a P-256 key, making what a browser hands back from
/// navigator.credentials.create and .get, so the real verification can be run against it.
/// </summary>
public sealed class SoftwareAuthenticator : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _rpId;
    private readonly string _origin;
    private uint _counter;

    public SoftwareAuthenticator(string rpId, string origin)
    {
        _rpId = rpId;
        _origin = origin;
    }

    public byte[] CredentialId { get; } = RandomNumberGenerator.GetBytes(32);
    public bool VerifiesUser { get; set; } = true;
    /// <summary>The origin the next ceremony claims; another one is refused by the server.</summary>
    public string? OriginOverride { get; set; }

    /// <summary>The attestation response JSON for options of navigator.credentials.create.</summary>
    public string Create(string optionsJson)
    {
        var challenge = ChallengeOf(optionsJson);
        var clientData = ClientData("webauthn.create", challenge);

        var credential = new List<byte>();
        credential.AddRange(new byte[16]); // AAGUID: none
        credential.AddRange(BigEndian((ushort)CredentialId.Length));
        credential.AddRange(CredentialId);
        credential.AddRange(CoseKey());
        var authData = AuthenticatorData(attested: true, credential.ToArray());

        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(3);
        writer.WriteTextString("fmt");
        writer.WriteTextString("none");
        writer.WriteTextString("attStmt");
        writer.WriteStartMap(0);
        writer.WriteEndMap();
        writer.WriteTextString("authData");
        writer.WriteByteString(authData);
        writer.WriteEndMap();

        return JsonSerializer.Serialize(new
        {
            id = B64(CredentialId),
            rawId = B64(CredentialId),
            type = "public-key",
            response = new { attestationObject = B64(writer.Encode()), clientDataJSON = B64(clientData) },
            clientExtensionResults = new { }
        });
    }

    /// <summary>The parts of an assertion for options of navigator.credentials.get.</summary>
    public (byte[] ClientDataJson, byte[] AuthenticatorData, byte[] Signature) Get(string challenge)
    {
        var clientData = ClientData("webauthn.get", challenge);
        var authData = AuthenticatorData(attested: false, []);
        var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
        var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return (clientData, authData, signature);
    }

    public static string B64(byte[] bytes) => Base64Url.EncodeToString(bytes);

    private static string ChallengeOf(string optionsJson) =>
        JsonDocument.Parse(optionsJson).RootElement.GetProperty("challenge").GetString()!;

    private byte[] ClientData(string type, string challenge) => Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        type,
        challenge,
        origin = OriginOverride ?? _origin,
        crossOrigin = false
    }));

    private byte[] AuthenticatorData(bool attested, byte[] attestedCredential)
    {
        byte flags = 0x01; // user present
        if (VerifiesUser)
            flags |= 0x04;
        if (attested)
            flags |= 0x40;

        _counter++;
        return SHA256.HashData(Encoding.UTF8.GetBytes(_rpId))
            .Append(flags)
            .Concat(BigEndian(_counter))
            .Concat(attestedCredential)
            .ToArray();
    }

    private byte[] CoseKey()
    {
        var parameters = _key.ExportParameters(false);
        var writer = new CborWriter(CborConformanceMode.Ctap2Canonical);
        writer.WriteStartMap(5);
        writer.WriteInt32(1);  // kty: EC2
        writer.WriteInt32(2);
        writer.WriteInt32(3);  // alg: ES256
        writer.WriteInt32(-7);
        writer.WriteInt32(-1); // crv: P-256
        writer.WriteInt32(1);
        writer.WriteInt32(-2); // x
        writer.WriteByteString(parameters.Q.X!);
        writer.WriteInt32(-3); // y
        writer.WriteByteString(parameters.Q.Y!);
        writer.WriteEndMap();
        return writer.Encode();
    }

    private static byte[] BigEndian(ushort value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] BigEndian(uint value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    public void Dispose() => _key.Dispose();
}

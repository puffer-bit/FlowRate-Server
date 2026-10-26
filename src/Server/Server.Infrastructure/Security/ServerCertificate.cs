using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Server.Infrastructure.Options;

namespace Server.Infrastructure.Security;

/// <summary>Loads the server certificate once and shares it between Kestrel and the coordinator gateways.</summary>
public class ServerCertificate
{
    private const string GeneratedCertificateFile = "server.crt";
    private const string GeneratedKeyFile = "server.key";
    private static readonly TimeSpan GeneratedLifetime = TimeSpan.FromDays(825);
    private static readonly TimeSpan RenewBefore = TimeSpan.FromDays(30);

    private readonly CertificateOptions _options;
    private readonly string _generatedDirectory;
    private readonly ILogger _logger;
    private readonly Lazy<X509Certificate2> _certificate;

    public ServerCertificate(IOptions<CertificateOptions> options, IOptions<StorageOptions> storage,
        ILogger<ServerCertificate> logger)
    {
        _options = options.Value;
        _generatedDirectory = storage.Value.CertificatesDirectory;
        _logger = logger;
        _certificate = new Lazy<X509Certificate2>(Load);
    }

    public X509Certificate2 Certificate => _certificate.Value;

    private X509Certificate2 Load()
    {
        if (!_options.IsConfigured)
            return LoadOrGenerate();

        var certificate = LoadPem(_options.Path!, _options.KeyPath!);
        _logger.LogInformation("Using certificate {Path} ({Subject}), valid until {NotAfter:yyyy-MM-dd}.",
            System.IO.Path.GetFullPath(_options.Path!), certificate.Subject, certificate.NotAfter);
        return certificate;
    }

    private static X509Certificate2 LoadPem(string certificatePath, string keyPath)
    {
        var certificateFile = System.IO.Path.GetFullPath(certificatePath);
        var keyFile = System.IO.Path.GetFullPath(keyPath);

        if (!File.Exists(certificateFile))
            throw new FileNotFoundException($"Server certificate not found: {certificateFile}", certificateFile);
        if (!File.Exists(keyFile))
            throw new FileNotFoundException($"Server certificate key not found: {keyFile}", keyFile);

        try
        {
            using var certificate = X509Certificate2.CreateFromPemFile(certificateFile, keyFile);
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException(
                $"Server certificate {certificateFile} or its key {keyFile} is not a valid PEM file: {ex.Message}", ex);
        }
    }

    private X509Certificate2 LoadOrGenerate()
    {
        var directory = System.IO.Path.GetFullPath(_generatedDirectory);
        var certificateFile = System.IO.Path.Combine(directory, GeneratedCertificateFile);
        var keyFile = System.IO.Path.Combine(directory, GeneratedKeyFile);

        if (File.Exists(certificateFile) && File.Exists(keyFile))
        {
            var existing = LoadPem(certificateFile, keyFile);
            if (existing.NotAfter.ToUniversalTime() - DateTime.UtcNow > RenewBefore)
            {
                _logger.LogInformation(
                    "No certificate configured, using the generated self-signed certificate {Path}. " +
                    "SHA-256 fingerprint: {Fingerprint}.", certificateFile, Fingerprint(existing));
                return existing;
            }

            _logger.LogWarning("The generated certificate {Path} expires on {NotAfter:yyyy-MM-dd}, creating a new one.",
                certificateFile, existing.NotAfter);
            existing.Dispose();
        }

        Generate(directory, certificateFile, keyFile);
        var generated = LoadPem(certificateFile, keyFile);
        _logger.LogWarning(
            "No certificate configured. Generated a self-signed certificate {Path}, valid until {NotAfter:yyyy-MM-dd}. " +
            "SHA-256 fingerprint: {Fingerprint}. Set Certificate:Path and Certificate:KeyPath to use your own.",
            certificateFile, generated.NotAfter, Fingerprint(generated));
        return generated;
    }

    private static void Generate(string directory, string certificateFile, string keyFile)
    {
        Directory.CreateDirectory(directory);

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={Environment.MachineName}, O=FlowRateServer", key, HashAlgorithmName.SHA256);

        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddDnsName(Environment.MachineName);
        foreach (var address in LocalAddresses())
            names.AddIpAddress(address);

        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var now = DateTimeOffset.UtcNow;
        using var certificate = request.CreateSelfSigned(now.AddDays(-1), now.Add(GeneratedLifetime));

        WriteFile(keyFile, key.ExportPkcs8PrivateKeyPem(), isSecret: true);
        WriteFile(certificateFile, certificate.ExportCertificatePem(), isSecret: false);
    }

    private static IEnumerable<IPAddress> LocalAddresses()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Loopback, IPAddress.IPv6Loopback };

        try
        {
            foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up))
            {
                foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily == AddressFamily.InterNetwork
                        || (address.AddressFamily == AddressFamily.InterNetworkV6 && !address.IsIPv6LinkLocal))
                        addresses.Add(address);
                }
            }
        }
        catch (NetworkInformationException)
        {
        }

        return addresses;
    }

    private static void WriteFile(string path, string content, bool isSecret)
    {
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
        if (isSecret && !OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        using var writer = new StreamWriter(path, options);
        writer.Write(content);
    }

    private static string Fingerprint(X509Certificate2 certificate) =>
        certificate.GetCertHashString(HashAlgorithmName.SHA256);
}

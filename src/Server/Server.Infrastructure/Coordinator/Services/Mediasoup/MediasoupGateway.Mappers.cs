using Server.Api.Grpc;
using Shared.Models.Media;
using Shared.Models.Media.Transport;

namespace Server.Infrastructure.Coordinator.Services.Mediasoup;

public partial class MediasoupGateway
{
    private static DTLSParameters MapProtoDtlsParametersToShared(DtlsParameters proto)
    {
        if (proto == null)
        {
            return null!;
        }

        return new DTLSParameters
        {
            Role = proto.Role,
            Fingerprints = proto.Fingerprints
                .Select(f => new DTLSFingerprint
                {
                    Algorithm = f.Algorithm,
                    Fingerprint = f.Value
                })
                .ToList()
        };
    }
    
    private static DtlsParameters MapSharedDtlsParametersToProto(DTLSParameters shared)
    {
        if (shared == null)
        {
            return null!;
        }

        var protoDtls = new Server.Api.Grpc.DtlsParameters
        {
            Role = shared.Role ?? "auto" 
        };

        if (shared.Fingerprints != null)
        {
            var protoFingerprints = shared.Fingerprints
                .Where(f => f != null)
                .Select(f => new Server.Api.Grpc.DtlsFingerprint
                {
                    Algorithm = f.Algorithm ?? "",
                    Value = f.Fingerprint ?? ""
                });
            
            protoDtls.Fingerprints.AddRange(protoFingerprints);
        }

        return protoDtls;
    }

    private static ICEParameters MapProtoIceParametersToShared(IceParameters proto)
    {
        if (proto == null)
        {
            return null!;
        }

        return new ICEParameters
        {
            UsernameFragment = proto.UsernameFragment,
            Password = proto.Password,
            ICECandidates = proto.ServerIceCandidates
                .Select(c => new ICECandidate
                {
                    Candidate = c.Candidate,
                    SDPMid = c.SdpMid,
                    SDPMLineIndex = c.SdpMLineIndex
                })
                .ToList()
        };
    }
}
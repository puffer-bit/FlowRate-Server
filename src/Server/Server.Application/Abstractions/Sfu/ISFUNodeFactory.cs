using Server.Application.Orchestrator.SFUPool.Models;

namespace Server.Application.Abstractions.Sfu;

public interface ISFUNodeFactory
{
    ISFUNode CreateSFUNode(SFUNodeModel model);
}
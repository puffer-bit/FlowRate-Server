using Microsoft.Extensions.Logging;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Abstractions.Factories;

public interface IComponentLoggerFactory
{
    ILogger Create(string componentName);
}
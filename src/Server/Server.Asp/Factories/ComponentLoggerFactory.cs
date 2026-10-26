using Serilog;
using Serilog.Extensions.Logging;
using Server.Application.Abstractions.Factories;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Server.Asp.Factories;

public class ComponentLoggerFactory : IComponentLoggerFactory
{
    private readonly SerilogLoggerFactory _serilogFactory;

    public ComponentLoggerFactory()
    {
        _serilogFactory = new SerilogLoggerFactory(Log.Logger);
    }

    public ILogger Create(string componentName)
    {
        var serilogLogger = Log.ForContext("Component", componentName);
        return new SerilogLoggerFactory(serilogLogger).CreateLogger(componentName);
    }
}
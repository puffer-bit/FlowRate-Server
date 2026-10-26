using Server.Core.Models.Coordinator;

namespace Server.Application.Coordinator;

public sealed class CoordinatorInstanceContext : ICoordinatorInstanceContext
{
    private volatile CoordinatorInstanceModel _model;

    public CoordinatorInstanceModel Model => _model;
    public string ServerVersion { get; }

    public CoordinatorInstanceContext(CoordinatorInstanceModel coordinatorInstanceModel, string serverVersion)
    {
        _model = coordinatorInstanceModel;
        ServerVersion = serverVersion;

        _model.MarkLaunched(DateTime.UtcNow);
    }

    public void ApplySettings(CoordinatorInstanceModel model)
    {
        if (model.Id != _model.Id)
            throw new ArgumentException($"Settings of coordinator {model.Id} cannot be applied to {_model.Id}.",
                nameof(model));

        model.MarkLaunched(_model.CurrentLaunchTime);
        _model = model;
    }
}

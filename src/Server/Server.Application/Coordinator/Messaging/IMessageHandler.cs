using Shared.Enums;
using Shared.Models;
using Server.Application.Coordinator.Events;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Sessions.Hybrid;

namespace Server.Application.Coordinator.Messaging;

public interface IMessageHandler
{
    Type PayloadType { get; }
    
    Task<HandleMessageResult> HandleMessage(object? data, string connectionId);

    BaseMessage? CreateFailureResponse(object data);
}

public interface IMessageHandler<TData> : IMessageHandler where TData : class
{
    Type IMessageHandler.PayloadType => typeof(TData);

    async Task<HandleMessageResult> IMessageHandler.HandleMessage(object? data, string connectionId)
    {
        return await HandleMessage(data as TData, connectionId);
    }

    BaseMessage? IMessageHandler.CreateFailureResponse(object data) =>
        data is TData typed ? CreateFailureResponse(typed) : null;

    Task<HandleMessageResult> HandleMessage(TData? data, string connectionId);

    BaseMessage? CreateFailureResponse(TData request) => null;
}

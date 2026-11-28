using Microsoft.Extensions.Logging.Abstractions;
using Shared.Enums;
using Shared.Models.Requests.Heartbeat;
using Shared.Serialization;
using Server.Infrastructure.Coordinator.Services;
using Xunit;

namespace Server.Infrastructure.Tests.Coordinator.Services;

public class MessageParserTests
{
    private const string Id = "bc52bace-965d-4ee8-afa6-0b7164708bb6";
    private readonly MessageParser _parser = new(NullLogger.Instance, ProtocolJsonContext.Default.Options);

    [Fact]
    public void AKnownMessage_IsReadWithoutRejection()
    {
        var message = _parser.ParseBaseMessage(
            System.Text.Encoding.UTF8.GetBytes($$"""{"$type":"HeartbeatPingRequest","RequestId":"{{Id}}"}"""), out var rejection);

        Assert.IsType<HeartbeatPingRequest>(message);
        Assert.Null(rejection);
    }

    [Fact]
    public void AnUnknownType_WithARequestId_IsAnsweredAsUnsupported()
    {
        var message = Parse($$"""{"$type":"FutureRequest","Nested":{"RequestId":"x"},"requestid":"{{Id}}"}""", out var rejection);

        Assert.Null(message);
        Assert.NotNull(rejection);
        Assert.Equal(ErrorCode.UnsupportedRequest, rejection.Code);
        Assert.Equal(Guid.Parse(Id), rejection.RequestId);
    }

    [Fact]
    public void AKnownType_ThatDoesNotRead_IsAnsweredAsInvalid()
    {
        Parse($$"""{"$type":"KickFromSessionRequest","SessionId":"one","RequestId":"{{Id}}"}""", out var rejection);

        Assert.Equal(ErrorCode.InvalidRequest, rejection?.Code);
    }

    [Theory]
    [InlineData("""{"$type":"FutureRequest"}""")]
    [InlineData("""{"$type":"FutureRequest","RequestId":"00000000-0000-0000-0000-000000000000"}""")]
    [InlineData("""{"RequestId":"bc52bace-965d-4ee8-afa6-0b7164708bb6"}""")]
    [InlineData("""[1,2]""")]
    [InlineData("""not json""")]
    public void WhatCannotBeNamed_IsNotAnswered(string json)
    {
        Assert.Null(Parse(json, out var rejection));
        Assert.Null(rejection);
    }

    private Shared.Models.BaseMessage? Parse(string json, out Shared.Models.Responses.ErrorResponse? rejection) =>
        _parser.ParseBaseMessage(System.Text.Encoding.UTF8.GetBytes(json), out rejection);
}

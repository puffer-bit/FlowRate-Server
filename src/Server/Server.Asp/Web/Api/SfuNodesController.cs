using Microsoft.AspNetCore.Mvc;
using Server.Application.Orchestrator.SFUPool;
using Server.Application.Orchestrator.Settings;
using Server.Asp.Web.Api.Contracts;
using Server.Core.Models.Orchestrator;

namespace Server.Asp.Web.Api;

[ApiController]
[Route(PanelRoutes.Api + "/sfu-nodes")]
[Produces("application/json")]
public class SfuNodesController(ISfuNodeStore store, ISfuNodeRegistry registry) : ControllerBase
{
    /// <summary>Add a node to the global pool. Assign it to coordinators separately.</summary>
    [HttpPost]
    [ProducesResponseType<SfuNodeDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SfuNodeDto>> Create(CreateSfuNodeDto request, CancellationToken ct)
    {
        var node = new SfuNodeSettingsModel
        {
            Name = request.Name.Trim(),
            Host = request.Host.Trim(),
            Port = request.Port,
            Username = request.Username?.Trim() ?? string.Empty,
            Key = request.Password ?? string.Empty,
            IsSecured = request.IsSecured
        };
        var id = await store.AddAsync(node, ct);
        return Created($"/api/panel/sfu-nodes", (node with { Id = id }).ToDto(registry.GetStatus(id)));
    }

    /// <summary>Replace or clear credentials for an existing node. The secret is never returned.</summary>
    [HttpPut("{id:int}/credentials")]
    [ProducesResponseType<SfuNodeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SfuNodeDto>> UpdateCredentials(int id, SfuCredentialsDto credentials, CancellationToken ct)
    {
        var node = (await store.GetAllAsync(ct)).FirstOrDefault(item => item.Id == id);
        if (node == null)
            return NotFound();

        var updated = node with { Username = credentials.Username?.Trim() ?? string.Empty, Key = credentials.Password ?? string.Empty };
        await store.UpdateAsync(updated, ct);
        return updated.ToDto(registry.GetStatus(id));
    }
}

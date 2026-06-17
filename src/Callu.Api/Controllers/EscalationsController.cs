using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Services;
using Callu.Shared.Models.Escalations;

namespace Callu.Api.Controllers;

[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/escalations")]
[Authorize]
public class EscalationsController(IEscalationService escalationService) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = Policies.CanViewEscalations)]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var policies = await escalationService.GetEscalationPoliciesAsync(ct);
        return Ok(policies);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.CanViewEscalations)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var policy = await escalationService.GetEscalationPolicyByIdAsync(id, ct);
        if (policy == null) return NotFound();
        return Ok(policy);
    }

    [HttpPost]
    [Authorize(Policy = Policies.CanManageEscalations)]
    public async Task<IActionResult> Create([FromBody] CreateEscalationRequest request, CancellationToken ct)
    {
        var policy = await escalationService.CreateEscalationPolicyAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = policy.Id }, policy);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.CanManageEscalations)]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateEscalationRequest request, CancellationToken ct)
    {
        var success = await escalationService.UpdateEscalationPolicyAsync(id, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = Policies.CanManageEscalations)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var success = await escalationService.DeleteEscalationPolicyAsync(id, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpGet("{id:guid}/steps")]
    [Authorize(Policy = Policies.CanViewEscalations)]
    public async Task<IActionResult> GetSteps(Guid id, CancellationToken ct)
    {
        var steps = await escalationService.GetEscalationStepsAsync(id, ct);
        return Ok(steps);
    }

    [HttpPost("{id:guid}/steps")]
    [Authorize(Policy = Policies.CanManageEscalations)]
    public async Task<IActionResult> AddStep(Guid id, [FromBody] CreateEscalationStepRequest request, CancellationToken ct)
    {
        var step = await escalationService.AddEscalationStepAsync(id, request, ct);
        return Ok(step);
    }

    [HttpDelete("{id:guid}/steps/{stepId:guid}")]
    [Authorize(Policy = Policies.CanManageEscalations)]
    public async Task<IActionResult> RemoveStep(Guid id, Guid stepId, CancellationToken ct)
    {
        var success = await escalationService.RemoveEscalationStepAsync(stepId, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPut("{id:guid}/steps/{stepId:guid}")]
    [Authorize(Policy = Policies.CanManageEscalations)]
    public async Task<IActionResult> UpdateStep(Guid id, Guid stepId,
        [FromBody] UpdateStepRequest request, CancellationToken ct)
    {
        var success = await escalationService.UpdateStepAsync(id, stepId, request, ct);
        if (!success) return NotFound();
        return NoContent();
    }

    [HttpPut("{id:guid}/steps/reorder")]
    [Authorize(Policy = Policies.CanManageEscalations)]
    public async Task<IActionResult> ReorderSteps(Guid id,
        [FromBody] ReorderStepsRequest request, CancellationToken ct)
    {
        var success = await escalationService.ReorderStepsAsync(id, request.StepIds, ct);
        if (!success) return NotFound();
        return NoContent();
    }
}

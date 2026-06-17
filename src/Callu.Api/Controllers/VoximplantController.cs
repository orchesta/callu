using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Callu.Application.Providers;
using Callu.Application.Services;
using Callu.Shared.Models.Communication;

namespace Callu.Api.Controllers;

/// <summary>
/// Voximplant management — account info, applications, rules, scenarios, users
/// </summary>
[ApiVersion(1)]
[ApiController]
[Route("api/v{version:apiVersion}/voximplant/management")]
[Authorize(Policy = Policies.CanManageIntegrations)]
public class VoximplantController(
    IVoximplantManagementService voxService,
    IEnumerable<ICommunicationProviderLifecycle> lifecycles) : ControllerBase
{
    private ICommunicationProviderLifecycle GetVoximplantLifecycle() =>
        lifecycles.First(l => l.ProviderType.Equals("voximplant", StringComparison.OrdinalIgnoreCase));
    [HttpGet("{providerId:guid}/account")]
    public async Task<IActionResult> GetAccountInfo(Guid providerId, CancellationToken ct)
    {
        var info = await voxService.GetAccountInfoAsync(providerId, ct);
        return Ok(info);
    }

    [HttpGet("{providerId:guid}/applications")]
    public async Task<IActionResult> GetApplications(Guid providerId, CancellationToken ct)
    {
        var apps = await voxService.GetApplicationsAsync(providerId, ct);
        return Ok(apps);
    }

    [HttpPost("{providerId:guid}/applications")]
    public async Task<IActionResult> CreateApplication(Guid providerId, [FromBody] CreateVoxApplicationRequest request, CancellationToken ct)
    {
        var app = await voxService.CreateApplicationAsync(providerId, request, ct);
        return Ok(app);
    }

    [HttpDelete("{providerId:guid}/applications/{applicationId:long}")]
    public async Task<IActionResult> DeleteApplication(Guid providerId, long applicationId, CancellationToken ct)
    {
        await voxService.DeleteApplicationAsync(providerId, applicationId, ct);
        return NoContent();
    }

    [HttpGet("{providerId:guid}/scenarios")]
    public async Task<IActionResult> GetScenarios(Guid providerId, [FromQuery] long? applicationId, CancellationToken ct)
    {
        var scenarios = await voxService.GetScenariosAsync(providerId, applicationId, ct);
        return Ok(scenarios);
    }

    [HttpPost("{providerId:guid}/scenarios")]
    public async Task<IActionResult> CreateScenario(Guid providerId, [FromBody] CreateVoxScenarioRequest request, CancellationToken ct)
    {
        var scenario = await voxService.CreateScenarioAsync(providerId, request, ct);
        return Ok(scenario);
    }

    [HttpGet("{providerId:guid}/applications/{applicationId:long}/rules")]
    public async Task<IActionResult> GetRules(Guid providerId, long applicationId, CancellationToken ct)
    {
        var rules = await voxService.GetRulesAsync(providerId, applicationId, ct);
        return Ok(rules);
    }

    [HttpPost("{providerId:guid}/rules")]
    public async Task<IActionResult> CreateRule(Guid providerId, [FromBody] CreateVoxRuleRequest request, CancellationToken ct)
    {
        var rule = await voxService.CreateRuleAsync(providerId, request, ct);
        return Ok(rule);
    }

    [HttpGet("{providerId:guid}/applications/{applicationId:long}/users")]
    public async Task<IActionResult> GetUsers(Guid providerId, long applicationId, CancellationToken ct)
    {
        var users = await voxService.GetUsersAsync(providerId, applicationId, ct);
        return Ok(users);
    }

    [HttpPost("{providerId:guid}/users")]
    public async Task<IActionResult> CreateUser(Guid providerId, [FromBody] CreateVoxUserRequest request, CancellationToken ct)
    {
        var user = await voxService.CreateUserAsync(providerId, request, ct);
        return Ok(user);
    }

    [HttpGet("{providerId:guid}/status")]
    public async Task<IActionResult> GetStatus(Guid providerId, CancellationToken ct)
    {
        var lifecycle = GetVoximplantLifecycle();
        var status = await lifecycle.GetStatusAsync(providerId, ct);
        return Ok(status);
    }
    
    [HttpPost("{providerId:guid}/provision")]
    public async Task<IActionResult> Provision(Guid providerId, CancellationToken ct)
    {
        var lifecycle = GetVoximplantLifecycle();
        var result = await lifecycle.ProvisionAsync(providerId, ct);
        return Ok(result);
    }

    [HttpPost("{providerId:guid}/sync-users")]
    public async Task<IActionResult> SyncUsers(Guid providerId, CancellationToken ct)
    {
        var lifecycle = GetVoximplantLifecycle();
        var result = await lifecycle.SyncUsersAsync(providerId, ct);
        return Ok(result);
    }
}

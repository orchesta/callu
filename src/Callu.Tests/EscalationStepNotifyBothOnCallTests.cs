using Callu.Domain.Entities;
using Callu.Infrastructure.Mapping;
using Callu.Shared.Models.Escalations;
using Mapster;
using Microsoft.Extensions.DependencyInjection;

namespace Callu.Tests;

/// <summary>
/// The NotifyBothOnCall flag, pinned end to end at the DTO layer: a checkbox the request DTO drops
/// is a secondary on-call nobody pages.
/// </summary>
public class EscalationStepNotifyBothOnCallTests
{
    static EscalationStepNotifyBothOnCallTests()
    {
        // Registers all TypeAdapterConfig mappings (global static config).
        new ServiceCollection().AddMappingConfig();
    }

    [Fact]
    public void StepDto_Mapping_CarriesNotifyBothOnCall()
    {
        var step = new EscalationStep
        {
            Id = Guid.NewGuid(),
            Title = "Primary + secondary",
            ScheduleId = Guid.NewGuid(),
            NotifyBothOnCall = true,
            TargetedUsers = new List<EscalationStepUser>(),
        };

        var dto = step.Adapt<EscalationStepDto>();

        Assert.True(dto.NotifyBothOnCall);
    }

    [Fact]
    public void StepDto_Mapping_DefaultsNotifyBothOnCallToFalse()
    {
        var step = new EscalationStep
        {
            Id = Guid.NewGuid(),
            Title = "Primary only",
            TargetedUsers = new List<EscalationStepUser>(),
        };

        var dto = step.Adapt<EscalationStepDto>();

        Assert.False(dto.NotifyBothOnCall);
    }

    [Fact]
    public void CreateRequest_DefaultsToFalse_SoExistingClientsAreUnchanged()
    {
        var request = new CreateEscalationStepRequest { Title = "t" };
        Assert.False(request.NotifyBothOnCall);
    }

    [Fact]
    public void UpdateRequest_DefaultsToNull_SoOmittingTheFieldLeavesTheStepUnchanged()
    {
        var request = new UpdateStepRequest();
        Assert.Null(request.NotifyBothOnCall);
    }
}

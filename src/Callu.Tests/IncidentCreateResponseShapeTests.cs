using Callu.Api.Controllers;
using Callu.Application.Services;
using Callu.Shared.Models.Incidents;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Callu.Tests;

/// <summary>Both outcomes of POST /incidents have to come back in the same envelope.</summary>
public class IncidentCreateResponseShapeTests
{
    private static IncidentsController Controller(IncidentCreateResult result)
    {
        var incidents = Substitute.For<IIncidentService>();
        incidents.CreateIncidentAsync(Arg.Any<CreateIncidentRequest>(), Arg.Any<CancellationToken>())
            .Returns(result);

        return new IncidentsController(
            incidents,
            Substitute.For<IIncidentNoteService>(),
            Substitute.For<ICallLogService>(),
            NullLogger<IncidentsController>.Instance);
    }

    private static CreateIncidentRequest Request() =>
        new() { Title = "DB unreachable", Severity = "High" };

    private static IncidentCreateResult Created(IncidentDto incident) =>
        new() { Outcome = IncidentCreateOutcome.Created, Incident = incident };

    private static IncidentCreateResult Suppressed(string reason) =>
        new() { Outcome = IncidentCreateOutcome.Suppressed, Reason = reason };

    [Fact]
    public async Task ACreatedIncident_ComesBackInTheOutcomeEnvelope()
    {
        var incident = new IncidentDto { Id = Guid.NewGuid(), Title = "DB unreachable" };
        var controller = Controller(Created(incident));

        var response = await controller.CreateIncident(Request(), CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(response);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);

        // The bare incident here is what left clients unable to read Outcome at all.
        var body = Assert.IsType<IncidentCreateResult>(created.Value);
        Assert.Equal(IncidentCreateOutcome.Created, body.Outcome);
        Assert.Equal(incident.Id, body.Incident!.Id);
    }

    [Fact]
    public async Task ASuppressedIncident_ComesBackInTheSameEnvelope_WithItsReason()
    {
        var controller = Controller(Suppressed("DB migration 02:00-04:00"));

        var response = await controller.CreateIncident(Request(), CancellationToken.None);

        var accepted = Assert.IsType<AcceptedResult>(response);
        Assert.Equal(StatusCodes.Status202Accepted, accepted.StatusCode);

        var body = Assert.IsType<IncidentCreateResult>(accepted.Value);
        Assert.Equal(IncidentCreateOutcome.Suppressed, body.Outcome);
        Assert.Null(body.Incident);
        Assert.Equal("DB migration 02:00-04:00", body.Reason);
    }

    [Fact]
    public async Task BothOutcomes_ReturnTheSameBodyType()
    {
        var createdBody = ((CreatedAtActionResult)await Controller(
                Created(new IncidentDto { Id = Guid.NewGuid(), Title = "t" }))
            .CreateIncident(Request(), CancellationToken.None)).Value;

        var suppressedBody = ((AcceptedResult)await Controller(
                Suppressed("window"))
            .CreateIncident(Request(), CancellationToken.None)).Value;

        Assert.Equal(createdBody!.GetType(), suppressedBody!.GetType());
    }
}

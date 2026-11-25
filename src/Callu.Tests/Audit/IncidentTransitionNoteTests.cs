using Callu.Shared.Exceptions;
using Callu.Shared.Models.Incidents;

namespace Callu.Tests.Audit;

public class IncidentTransitionNoteTests
{
    [Fact]
    public void WhatAPersonTyped_IsTrimmed_AndBlankIsNothing()
    {
        var note = IncidentTransitionNote.FromInput("  permanent-fix ", "  Disk temizlendi.  ");

        Assert.Equal("permanent-fix", note.ResolutionType);
        Assert.Equal("Disk temizlendi.", note.Reason);
        Assert.Null(IncidentTransitionNote.FromInput(" ", " ").Reason);
        Assert.Null(IncidentTransitionNote.FromInput(null, null).ResolutionType);
    }

    [Fact]
    public void AnUnknownResolutionType_AndAnOverlongReason_AreRefusedByField()
    {
        var ex = Assert.Throws<ValidationException>(() =>
            IncidentTransitionNote.FromInput("fixed-itself", new string('x', IncidentTransitionNote.MaxReasonLength + 1)));

        Assert.Contains("resolutionType", ex.Errors!.Keys);
        Assert.Contains("reason", ex.Errors!.Keys);
    }

    [Fact]
    public void AlertCleared_IsNotSomethingAPersonCanType()
    {
        Assert.Throws<ValidationException>(() => IncidentTransitionNote.FromInput(IncidentTransitionNote.AlertCleared, null));
    }
}

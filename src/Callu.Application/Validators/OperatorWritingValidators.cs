using FluentValidation;
using Callu.Domain.Entities;
using Callu.Shared.Models.Incidents;
using Callu.Shared.Models.Postmortems;
using Callu.Shared.Models.Runbooks;

namespace Callu.Application.Validators;

/// <summary>
/// Length rules for operator-written postmortem fields, taken from the entity's own limits.
/// </summary>
public class CreatePostmortemRequestValidator : AbstractValidator<CreatePostmortemRequest>
{
    public CreatePostmortemRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(Postmortem.MaxTitleLength);

        RuleFor(x => x.RootCause)
            .MaximumLength(Postmortem.MaxRootCauseLength);
    }
}

public class UpdatePostmortemRequestValidator : AbstractValidator<UpdatePostmortemRequest>
{
    public UpdatePostmortemRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(Postmortem.MaxTitleLength);

        RuleFor(x => x.RootCause)
            .MaximumLength(Postmortem.MaxRootCauseLength);
    }
}

public class CreateRunbookRequestValidator : AbstractValidator<CreateRunbookRequest>
{
    public CreateRunbookRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(Runbook.MaxTitleLength);

        RuleFor(x => x.Description)
            .MaximumLength(Runbook.MaxDescriptionLength);
    }
}

public class UpdateRunbookRequestValidator : AbstractValidator<UpdateRunbookRequest>
{
    public UpdateRunbookRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required")
            .MaximumLength(Runbook.MaxTitleLength);

        RuleFor(x => x.Description)
            .MaximumLength(Runbook.MaxDescriptionLength);
    }
}

public class CreateIncidentNoteRequestValidator : AbstractValidator<CreateIncidentNoteRequest>
{
    public CreateIncidentNoteRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Note content is required")
            .MaximumLength(IncidentNote.MaxContentLength);
    }
}

public class UpdateIncidentNoteRequestValidator : AbstractValidator<UpdateIncidentNoteRequest>
{
    public UpdateIncidentNoteRequestValidator()
    {
        RuleFor(x => x.Content)
            .NotEmpty().WithMessage("Note content is required")
            .MaximumLength(IncidentNote.MaxContentLength);
    }
}

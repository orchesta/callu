using Callu.Shared;

namespace Callu.Shared.Models.Conference;

public class ConferenceRoomFilter
{
    public int Page { get; set; } = 1;

    /// <summary>C# 14 <c>field</c>-backed setter: clamps to pagination constants.</summary>
    public int PageSize
    {
        get;
        set => field = value <= 0
            ? AppConstants.Pagination.DefaultPageSize
            : Math.Min(value, AppConstants.Pagination.MaxPageSize);
    } = AppConstants.Pagination.DefaultPageSize;
    
    public string? Status { get; set; }
    public Guid? IncidentId { get; set; }

    public bool? HasRecording { get; set; }
}

namespace Callu.Domain.Enums;

/// <summary>
/// Rotation recurrence type
/// </summary>
public enum RecurrenceType
{
    /// <summary>
    /// No recurrence - one-time rotation
    /// </summary>
    None = 0,

    /// <summary>
    /// Repeats daily
    /// </summary>
    Daily = 1,

    /// <summary>
    /// Repeats weekly
    /// </summary>
    Weekly = 2,

    /// <summary>
    /// Repeats every two weeks
    /// </summary>
    Biweekly = 3,

    /// <summary>
    /// Repeats monthly
    /// </summary>
    Monthly = 4
}

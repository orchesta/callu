using Callu.Shared.Models.Communication;

namespace Callu.Application.Services;

/// <summary>
/// Service for managing SIP trunk configurations
/// </summary>
public interface ISipTrunkService
{
    /// <summary>
    /// Get all SIP trunks
    /// </summary>
    Task<IEnumerable<SipTrunkDto>> GetTrunksAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a specific SIP trunk by ID
    /// </summary>
    Task<SipTrunkDto?> GetTrunkAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a new SIP trunk
    /// </summary>
    Task<SipTrunkDto> CreateTrunkAsync(CreateSipTrunkRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update a SIP trunk configuration
    /// </summary>
    Task UpdateTrunkAsync(Guid id, UpdateSipTrunkRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a SIP trunk (soft delete)
    /// </summary>
    Task DeleteTrunkAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Test a SIP trunk connection
    /// </summary>
    Task<(bool Success, string Message)> TestTrunkAsync(Guid id, CancellationToken cancellationToken = default);
}

using Callu.Shared.Models.Communication;

namespace Callu.Application.Services;

/// <summary>
/// Service for managing communication providers
/// </summary>
public interface ICommunicationProviderService
{
    /// <summary>
    /// Get all configured providers
    /// </summary>
    Task<IEnumerable<CommunicationProviderDto>> GetProvidersAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get a specific provider by ID
    /// </summary>
    Task<CommunicationProviderDto?> GetProviderAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Create a new provider configuration
    /// </summary>
    Task<CommunicationProviderDto> CreateProviderAsync(CreateProviderRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Update a provider configuration
    /// </summary>
    Task UpdateProviderAsync(Guid id, UpdateProviderRequest request, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Delete a provider (soft delete)
    /// </summary>
    Task DeleteProviderAsync(Guid id, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Send a one-off test SMS through the provider to verify configuration end-to-end.
    /// </summary>
    Task<SmsResult> SendTestSmsAsync(Guid id, string to, string? message, CancellationToken cancellationToken = default);
}

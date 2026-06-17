using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Mapster;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Providers.Voximplant;
using Callu.Shared.Models.Communication;

namespace Callu.Infrastructure.Services;

/// <summary>
/// Service for managing SIP trunk configurations
/// </summary>
public class SipTrunkService : ISipTrunkService
{
    private readonly ISipTrunkSettingsRepository _trunkRepo;
    private readonly ICommunicationProviderRepository _providerRepo;
    private readonly ITransactionManager _transactionManager;
    private readonly SipTrunkPasswordProtector _passwordProtector;
    private readonly ILogger<SipTrunkService> _logger;

    public SipTrunkService(
        ISipTrunkSettingsRepository trunkRepo,
        ICommunicationProviderRepository providerRepo,
        ITransactionManager transactionManager,
        SipTrunkPasswordProtector passwordProtector,
        ILogger<SipTrunkService> logger)
    {
        _trunkRepo = trunkRepo;
        _providerRepo = providerRepo;
        _transactionManager = transactionManager;
        _passwordProtector = passwordProtector;
        _logger = logger;
    }
    
    public async Task<IEnumerable<SipTrunkDto>> GetTrunksAsync(CancellationToken cancellationToken = default)
    {
        var trunks = await _trunkRepo.FindAsync(t => !t.IsDeleted, cancellationToken);
        return trunks.OrderBy(t => t.Name).Select(t => t.Adapt<SipTrunkDto>());
    }
    
    public async Task<SipTrunkDto?> GetTrunkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (trunk == null) return null;
        
        return trunk.Adapt<SipTrunkDto>();
    }
    
    public async Task<SipTrunkDto> CreateTrunkAsync(CreateSipTrunkRequest request, CancellationToken cancellationToken = default)
    {
        var trunkId = await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var trunk = new SipTrunkSettings
            {
                Name = request.Name,
                Server = request.Server,
                Port = request.Port,
                Username = request.Username,
                Password = _passwordProtector.Protect(request.Password),
                AuthUser = request.AuthUser,
                CallerId = request.CallerId,
                DisplayName = request.DisplayName,
                UseTls = request.UseTls,
                UseTcp = request.UseTcp,
                IsEnabled = true
            };
            
            await _trunkRepo.AddAsync(trunk, cancellationToken);
            
            _logger.LogInformation("Created SIP trunk: {Name} ({Server})", trunk.Name, trunk.Server);
            
            return trunk.Id;
        }, cancellationToken);
        
        return (await GetTrunkAsync(trunkId, cancellationToken))!;
    }
    
    public async Task UpdateTrunkAsync(Guid id, UpdateSipTrunkRequest request, CancellationToken cancellationToken = default)
    {
        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
            if (trunk == null)
                throw new KeyNotFoundException($"SIP trunk not found: {id}");
            
            trunk.Name = request.Name;
            trunk.Server = request.Server;
            trunk.Port = request.Port;
            trunk.Username = request.Username;
            trunk.AuthUser = request.AuthUser;
            trunk.CallerId = request.CallerId;
            trunk.DisplayName = request.DisplayName;
            trunk.UseTls = request.UseTls;
            trunk.UseTcp = request.UseTcp;
            trunk.IsEnabled = request.IsEnabled;
            
            if (!string.IsNullOrEmpty(request.Password))
            {
                trunk.Password = _passwordProtector.Protect(request.Password);
            }
            
            _logger.LogInformation("Updated SIP trunk: {Name}", trunk.Name);
            return true;
        }, cancellationToken);
    }
    
    public async Task DeleteTrunkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
            if (trunk == null)
                throw new KeyNotFoundException($"SIP trunk not found: {id}");

            var usedBy = await _providerRepo.GetQueryable()
                .Where(p => p.SipTrunkId == id && !p.IsDeleted)
                .Select(p => p.Name)
                .ToListAsync(cancellationToken);
            
            if (usedBy.Any())
            {
                throw new InvalidOperationException(
                    $"Cannot delete SIP trunk - used by providers: {string.Join(", ", usedBy)}");
            }
            
            trunk.IsDeleted = true;
            
            _logger.LogInformation("Deleted SIP trunk: {Name}", trunk.Name);
            return true;
        }, cancellationToken);
    }
    
    public async Task<(bool Success, string Message)> TestTrunkAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var trunk = await _trunkRepo.FindSingleAsync(t => t.Id == id && !t.IsDeleted, cancellationToken);
        if (trunk == null)
            return (false, "SIP trunk not found");
        
        try
        {
            if (string.IsNullOrEmpty(trunk.Server))
                return (false, "Server address is required");
                
            if (string.IsNullOrEmpty(trunk.Username))
                return (false, "Username is required");
                
            if (trunk.Port <= 0 || trunk.Port > 65535)
                return (false, "Invalid port number");
            
            var protocol = trunk.UseTls ? "sips" : "sip";
            var transport = trunk.UseTcp ? ";transport=tcp" : "";
            var sipUri = $"{protocol}:{trunk.Username}@{trunk.Server}:{trunk.Port}{transport}";

            return (true, $"Configuration is valid (format check only — no live SIP connection was attempted). SIP URI: {sipUri}");
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex, "Failed to validate SIP trunk: {Name}", trunk.Name);
            return (false, $"Validation failed: {ex.Message}");
        }
    }
}

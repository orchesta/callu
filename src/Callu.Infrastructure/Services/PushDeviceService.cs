using Microsoft.EntityFrameworkCore;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Services;
using Callu.Domain.Entities;
using Callu.Infrastructure.Persistence.Transactions;
using Callu.Shared.Models.Devices;

namespace Callu.Infrastructure.Services;

public class PushDeviceService(
    IUserPushDeviceRepository devices,
    ITransactionManager transactionManager) : IPushDeviceService
{
    public async Task<PushDeviceDto> RegisterAsync(
        string userId, RegisterPushDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var platform = PushPlatforms.Normalize(request.Platform);
        var token = request.PushToken.Trim();

        try
        {
            return await RegisterCoreAsync(userId, platform, token, cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Registering a device twice is the same request said twice, so the row the other one
            // wrote is the answer. Read it rather than retry: the aborted transaction is already gone.
            var winner = await devices.GetByTokenAsync(token, cancellationToken);
            if (winner is null) throw;

            return ToDto(winner);
        }
    }

    private async Task<PushDeviceDto> RegisterCoreAsync(
        string userId, string platform, string token, CancellationToken cancellationToken)
    {
        return await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            var existing = await devices.GetByTokenAsync(token, cancellationToken);
            if (existing is not null)
            {
                existing.UserId = userId;
                existing.Platform = platform;
                existing.LastSeenAt = DateTime.UtcNow;
                existing.UpdatedAt = DateTime.UtcNow;
                return ToDto(existing);
            }

            // Deletes are not flushed until the transaction commits, so the surplus has to be
            // picked from one ordered read rather than re-counting after each delete.
            var owned = await devices.GetActiveByUserIdAsync(userId, cancellationToken);
            foreach (var surplus in owned.Skip(UserPushDevice.MaxDevicesPerUser - 1))
                devices.Delete(surplus);

            var entity = new UserPushDevice
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Platform = platform,
                PushToken = token,
                LastSeenAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            await devices.AddAsync(entity, cancellationToken);
            return ToDto(entity);
        }, cancellationToken);
    }

    public async Task UnregisterAsync(
        string userId, UnregisterPushDeviceRequest? request, CancellationToken cancellationToken = default)
    {
        await transactionManager.ExecuteInTransactionAsync(async () =>
        {
            if (!string.IsNullOrWhiteSpace(request?.PushToken))
            {
                var one = await devices.GetByTokenAsync(request.PushToken.Trim(), cancellationToken);
                if (one is not null && one.UserId == userId)
                    devices.Delete(one);
                return true;
            }

            var all = await devices.GetActiveByUserIdAsync(userId, cancellationToken);
            foreach (var d in all)
                devices.Delete(d);
            return true;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PushDeviceDto>> ListAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var list = await devices.GetActiveByUserIdAsync(userId, cancellationToken);
        return list.Select(ToDto).ToList();
    }

    private static PushDeviceDto ToDto(UserPushDevice d) => new()
    {
        Id = d.Id,
        Platform = d.Platform,
        LastSeenAt = d.LastSeenAt
    };
}

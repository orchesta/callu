using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Callu.Infrastructure.Persistence.Repositories;

public class TenantUserReadRepository(ApplicationDbContext context) : ITenantUserReadRepository
{
    public async Task<IReadOnlyList<TenantUserDirectoryRow>> GetDirectoryUsersAsync(
        CancellationToken cancellationToken = default)
    {
        var users = await context.Users
            .AsNoTracking()
            .Where(u => !u.IsDeleted)
            .OrderBy(u => u.DisplayName)
            .Select(u => new
            {
                u.Id,
                u.Email,
                u.DisplayName,
                u.FirstName,
                u.LastName,
                u.PhoneNumber,
                u.Timezone,
                u.EmailConfirmed,
                u.CreatedAt,
                u.LastLoginAt,
                u.IsDeleted
            })
            .ToListAsync(cancellationToken);

        if (users.Count == 0)
            return [];

        var userIds = users.Select(u => u.Id).ToList();
        var rolePairs = await (
                from ur in context.UserRoles.AsNoTracking()
                join r in context.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where userIds.Contains(ur.UserId)
                select new { ur.UserId, RoleName = r.Name })
            .ToListAsync(cancellationToken);

        var roleByUser = rolePairs
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.First().RoleName ?? "Member");

        return users.Select(u => new TenantUserDirectoryRow(
                u.Id,
                u.Email ?? "",
                u.DisplayName,
                u.FirstName,
                u.LastName,
                u.PhoneNumber,
                u.Timezone,
                u.EmailConfirmed,
                u.CreatedAt,
                u.LastLoginAt,
                u.IsDeleted,
                roleByUser.GetValueOrDefault(u.Id, "Member")))
            .ToList();
    }
}

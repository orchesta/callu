using System.Linq.Expressions;
using Callu.Application.Common.Interfaces.Persistence;
using Callu.Application.Common.Models.Persistence;
using Callu.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;

namespace Callu.Infrastructure.Persistence.Repositories;

public class UserContactRepository(ApplicationDbContext context) : IUserContactRepository
{
    private static readonly Expression<Func<ApplicationUser, UserContactSnapshot>> ToSnapshot =
        u => new UserContactSnapshot(u.Id, u.DisplayName, u.PhoneNumber, u.Email, u.Culture);

    public async Task<IReadOnlyList<UserContactSnapshot>> GetContactsByIdsAsync(
        IReadOnlyCollection<string> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
            return [];

        return await context.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id) && !u.IsDeleted)
            .Select(ToSnapshot)
            .ToListAsync(cancellationToken);
    }

    public async Task<UserContactSnapshot?> GetContactByIdAsync(string userId, CancellationToken cancellationToken = default) =>
        await context.Users
            .AsNoTracking()
            .Where(u => u.Id == userId && !u.IsDeleted)
            .Select(ToSnapshot)
            .FirstOrDefaultAsync(cancellationToken);
}

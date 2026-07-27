using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using AskLucy.Persistence.Identity;
using AutoMapper;
using AutoMapper.QueryableExtensions;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserAdminRepository(AskLucyDbContext dbContext, IMapper mapper) : IUserAdminRepository
{
    public async Task<IReadOnlyList<UserAdminDto>> ListAllAsync(CancellationToken cancellationToken = default) =>
        await dbContext.Users.ProjectTo<UserAdminDto>(mapper.ConfigurationProvider).ToListAsync(cancellationToken);

    public async Task<UserAdminDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user is null ? null : mapper.Map<UserAdminDto>(user);
    }

    public async Task<bool> UpdateAsync(string userId, string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
        {
            return false;
        }

        // Only these two fields are ever written from a PATCH request — the source
        // request body's other properties (id, passwordHash, roles, etc.) are never
        // even read this far up the stack. This is what closes the mass-assignment gap.
        user.FirstName = firstName;
        user.LastName = lastName;

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

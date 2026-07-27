using AskLucy.Application.Abstractions;
using AskLucy.Application.Users;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Persistence.Repositories;

public sealed class UserProfileRepository(AskLucyDbContext dbContext) : IUserProfileRepository
{
    public async Task<UserProfileDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null
            ? null
            : new UserProfileDto(
                user.Id, user.Email ?? string.Empty, user.FirstName, user.LastName,
                user.BirthDate, user.TwoFactorEnabled, user.AvatarFileName);
    }

    public async Task UpdateAsync(string userId, string? firstName, string? lastName, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        user.FirstName = firstName;
        user.LastName = lastName;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetAvatarFileNameAsync(string userId, string? avatarFileName, CancellationToken cancellationToken = default)
    {
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new KeyNotFoundException("User not found.");

        user.AvatarFileName = avatarFileName;
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

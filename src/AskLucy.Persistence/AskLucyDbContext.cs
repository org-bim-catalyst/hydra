using System.Reflection;
using AskLucy.Domain.Authentication;
using AskLucy.Domain.Chats;
using AskLucy.Persistence.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AskLucy.Persistence;

/// <summary>
/// Migrated from the legacy <c>ChatGPT_ClientContext</c>. Same physical database
/// (connection string key <c>DefaultConnection</c>) so existing production data
/// is migrated in place, per spec.md FR-014/SC-009.
/// </summary>
public sealed class AskLucyDbContext(DbContextOptions<AskLucyDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<UserChat> UserChats => Set<UserChat>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Deliberately no HasData() seeding here: unlike the legacy ChatGPT_ClientContext,
        // this migration does not re-author a hardcoded seed-admin credential in new code.
        // Existing production users/roles are carried over via the in-place migration
        // (research.md Topic 5), not re-seeded — see spec.md's Assumptions/Risks.
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<DateOnly>()
            .HaveConversion<DateOnlyConverter>()
            .HaveColumnType("date");

        configurationBuilder.Properties<DateOnly?>()
            .HaveConversion<NullableDateOnlyConverter>()
            .HaveColumnType("date");
    }
}

/// <summary>Converts <see cref="DateOnly"/> to <see cref="DateTime"/> and back (unchanged from the legacy context).</summary>
public sealed class DateOnlyConverter() : ValueConverter<DateOnly, DateTime>(
    d => d.ToDateTime(TimeOnly.MinValue),
    d => DateOnly.FromDateTime(d));

public sealed class NullableDateOnlyConverter() : ValueConverter<DateOnly?, DateTime?>(
    d => d == null ? null : new DateTime?(d.Value.ToDateTime(TimeOnly.MinValue)),
    d => d == null ? null : new DateOnly?(DateOnly.FromDateTime(d.Value)));

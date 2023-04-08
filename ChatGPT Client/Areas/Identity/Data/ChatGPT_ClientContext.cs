using AskLucy.Areas.Identity.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.ComponentModel;

namespace AskLucy.Data;

public class ChatGPT_ClientContext : IdentityDbContext<ApplicationUser>
{
    public ChatGPT_ClientContext(DbContextOptions<ChatGPT_ClientContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // Customize the ASP.NET Identity model and override the defaults if needed.
        // For example, you can rename the ASP.NET Identity table names and more.
        // Add your customizations after calling base.OnModelCreating(builder);
        this.SeedUsers(builder);
        this.SeedRoles(builder);        
        this.SeedUserRoles(builder);

    }
    private void SeedUsers(ModelBuilder builder)
    {
        //https://frankofoedu.medium.com/how-to-seed-identity-role-with-associated-user-in-asp-net-core-ef-core-e40ecd643d0f
        ApplicationUser user = new ApplicationUser()
        {
            Id = "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
            UserName = "mustafa.salaheldin@yahoo.com",
            NormalizedUserName = "MUSTAFA.SALAHELDIN@YAHOO.COM",
            Email = "mustafa.salaheldin@yahoo.com",
            NormalizedEmail = "MUSTAFA.SALAHELDIN@YAHOO.COM",
            LockoutEnabled = false,
            PhoneNumber = "00971501342563",
            FirstName="Mustafa",
            LastName="Ali",
            BirthDate= new DateOnly(1981,9,1),
            EmailConfirmed=true

        };

        PasswordHasher<ApplicationUser> passwordHasher = new PasswordHasher<ApplicationUser>();
        
        user.PasswordHash = passwordHasher.HashPassword(user, "Ms@191981");

        builder.Entity<ApplicationUser>().HasData(user);
    }

    private void SeedRoles(ModelBuilder builder)
    {
        builder.Entity<IdentityRole>().HasData(
            new IdentityRole() { Id = "d12a6772-02ae-41e6-8448-3b19049b313a", Name = "Super User", ConcurrencyStamp = "1", NormalizedName = "Super User" },
            new IdentityRole() { Id = "dc656fc4-221b-44ed-9373-47daec554bd1", Name = "Administrator", ConcurrencyStamp = "2", NormalizedName = "Administrator" }

            );
    }

    private void SeedUserRoles(ModelBuilder builder)
    {
        builder.Entity<IdentityUserRole<string>>().HasData(new IdentityUserRole<string>() { RoleId = "d12a6772-02ae-41e6-8448-3b19049b313a", UserId = "0eb8f096-33c7-45c5-9160-fd9cdd053e97" });
        builder.Entity<IdentityUserRole<string>>().HasData(new IdentityUserRole<string>() { RoleId = "dc656fc4-221b-44ed-9373-47daec554bd1", UserId = "0eb8f096-33c7-45c5-9160-fd9cdd053e97" });
    }

    //https://github.com/dotnet/efcore/issues/24507
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        builder.Properties<DateOnly>()
            .HaveConversion<DateOnlyConverter>()
            .HaveColumnType("date");

        builder.Properties<DateOnly?>()
            .HaveConversion<NullableDateOnlyConverter>()
            .HaveColumnType("date");
    }


}

/// <summary>
/// Converts <see cref="DateOnly" /> to <see cref="DateTime"/> and vice versa.
/// </summary>
public class DateOnlyConverter : ValueConverter<DateOnly, DateTime>
{
    /// <summary>
    /// Creates a new instance of this converter.
    /// </summary>
    public DateOnlyConverter() : base(
            d => d.ToDateTime(TimeOnly.MinValue),
            d => DateOnly.FromDateTime(d))
    { }
}

/// <summary>
/// Converts <see cref="DateOnly?" /> to <see cref="DateTime?"/> and vice versa.
/// </summary>
public class NullableDateOnlyConverter : ValueConverter<DateOnly?, DateTime?>
{
    /// <summary>
    /// Creates a new instance of this converter.
    /// </summary>
    public NullableDateOnlyConverter() : base(
        d => d == null
            ? null
            : new DateTime?(d.Value.ToDateTime(TimeOnly.MinValue)),
        d => d == null
            ? null
            : new DateOnly?(DateOnly.FromDateTime(d.Value)))
    { }
}
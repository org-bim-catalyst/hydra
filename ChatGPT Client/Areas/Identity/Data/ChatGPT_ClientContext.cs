using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AskLucy.Data;

public class ChatGPT_ClientContext : IdentityDbContext<IdentityUser>
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
        IdentityUser user = new IdentityUser()
        {
            Id = "0eb8f096-33c7-45c5-9160-fd9cdd053e97",
            UserName = "mustafa.salaheldin@yahoo.com",
            Email = "mustafa.salaheldin@yahoo.com",
            LockoutEnabled = false,
            PhoneNumber = "00971501342563"
        };

        PasswordHasher<IdentityUser> passwordHasher = new PasswordHasher<IdentityUser>();
        
        user.PasswordHash = passwordHasher.HashPassword(user, "Ms@191981");
        user.NormalizedEmail = user.Email.ToUpper();
        user.NormalizedUserName = user.UserName.ToUpper();

        builder.Entity<IdentityUser>().HasData(user);
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
}

using AskLucy.Domain.Projects;
using AskLucy.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AskLucy.Persistence.Configurations.Projects;

public sealed class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> builder)
    {
        builder.ToTable("Projects");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.UserId).IsRequired();
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);

        builder.Property(p => p.CreatedBy).IsRequired();
        builder.Property(p => p.RowVersion).IsRowVersion();

        builder.HasQueryFilter(p => p.DeletedAtUtc == null);

        builder.HasIndex(p => p.UserId);

        // Cascades directly from ApplicationUser (spec.md FR-026's account-deletion cleanup
        // reasonably extends to a user's Projects too, not only Memory rows) — see
        // MemoryConfiguration's remarks for why Memory.ProjectId is Restrict, not a second cascade
        // path, avoiding SQL Server's multiple-cascade-paths validation.
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

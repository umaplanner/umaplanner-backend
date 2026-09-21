using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;

namespace UmaPlanner.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserOptions> Users => Set<UserOptions>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserOptions>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(user => user.Id);
            entity.HasIndex(user => user.DiscordId).IsUnique();
            entity.Property(user => user.Id).HasMaxLength(128);
            entity.Property(user => user.DiscordId).HasMaxLength(128).IsRequired();
            entity.Property(user => user.Username).HasMaxLength(128).IsRequired();
            entity.Property(user => user.AvatarUrl).HasMaxLength(512);
            entity.Property(user => user.TrainerId).HasMaxLength(128);
            entity.Property(user => user.CreatedAt).HasMaxLength(64).IsRequired();
        });
    }
}

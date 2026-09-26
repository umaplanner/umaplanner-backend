using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;

namespace UmaPlanner.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserOptions> Users => Set<UserOptions>();
    public DbSet<UserUmaBuild> UmaBuilds => Set<UserUmaBuild>();
    public DbSet<UserTeam> Teams => Set<UserTeam>();

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

        modelBuilder.Entity<UserUmaBuild>(entity =>
        {
            entity.ToTable("user_uma_builds");
            entity.HasKey(build => new { build.UserId, build.Event, build.Id });
            entity.Property(build => build.UserId).HasMaxLength(128).IsRequired();
            entity.Property(build => build.Event).HasMaxLength(128).IsRequired();
            entity.Property(build => build.Id).HasMaxLength(128).IsRequired();
            entity.Property(build => build.Data).HasColumnType("jsonb").IsRequired();
            entity.Property(build => build.DeletedAt).HasColumnType("timestamp with time zone");
            entity.HasOne<UserOptions>()
                .WithMany()
                .HasForeignKey(build => build.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserTeam>(entity =>
        {
            entity.ToTable("user_teams");
            entity.HasKey(team => new { team.UserId, team.Event });
            entity.Property(team => team.UserId).HasMaxLength(128).IsRequired();
            entity.Property(team => team.Event).HasMaxLength(128).IsRequired();
            entity.Property(team => team.Data).HasColumnType("jsonb").IsRequired();
            entity.HasOne<UserOptions>()
                .WithMany()
                .HasForeignKey(team => team.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

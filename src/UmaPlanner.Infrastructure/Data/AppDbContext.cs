using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using UmaPlanner.Core.Entities;
using UmaPlanner.Infrastructure.Data.Admin;

namespace UmaPlanner.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    public DbSet<UserOptions> Users => Set<UserOptions>();
    public DbSet<UserUmaBuild> UmaBuilds => Set<UserUmaBuild>();
    public DbSet<UserTeam> Teams => Set<UserTeam>();
    public DbSet<UserResult> Results => Set<UserResult>();
    public DbSet<DailyStat> DailyStats => Set<DailyStat>();

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

        modelBuilder.Entity<UserResult>(entity =>
        {
            entity.ToTable("user_results");
            entity.HasKey(result => new { result.UserId, result.Event });
            entity.Property(result => result.UserId).HasMaxLength(128).IsRequired();
            entity.Property(result => result.Event).HasMaxLength(128).IsRequired();
            entity.Property(result => result.Data).HasColumnType("jsonb").IsRequired();
            entity.HasOne<UserOptions>()
                .WithMany()
                .HasForeignKey(result => result.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DailyStat>(entity =>
        {
            entity.ToTable("admin_daily_stats");
            entity.HasKey(stat => new { stat.Date, stat.Event });
            entity.Property(stat => stat.Date).HasColumnType("date");
            entity.Property(stat => stat.Event).HasMaxLength(128).IsRequired();
            entity.Property(stat => stat.UserCount).IsRequired();
            entity.Property(stat => stat.BuildCount).IsRequired();
            entity.Property(stat => stat.TeamCount).IsRequired();
        });
    }
}

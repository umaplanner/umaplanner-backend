using Microsoft.EntityFrameworkCore;
using UmaPlanner.Core.Entities;

namespace UmaPlanner.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<Item> Items => Set<Item>();
    // public DbSet<Image> Images => Set<Image>();
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace UmaPlanner.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261010170000_AddAdminDailyStats")]
public partial class AddAdminDailyStats : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "admin_daily_stats",
            columns: table => new
            {
                Date = table.Column<DateOnly>(type: "date", nullable: false),
                Event = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                UserCount = table.Column<long>(type: "bigint", nullable: false),
                BuildCount = table.Column<long>(type: "bigint", nullable: false),
                TeamCount = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_admin_daily_stats", x => new { x.Date, x.Event });
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "admin_daily_stats");
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011151000_AddAnonymousTeamContributions")]
public partial class AddAnonymousTeamContributions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "anonymous_team_contributions",
            columns: table => new
            {
                AnonymousId = table.Column<string>(
                    type: "character varying(36)",
                    maxLength: 36,
                    nullable: false),
                Event = table.Column<string>(
                    type: "character varying(128)",
                    maxLength: 128,
                    nullable: false),
                TeamData = table.Column<string>(type: "jsonb", nullable: false),
                BuildsData = table.Column<string>(type: "jsonb", nullable: false),
                LastUpdate = table.Column<long>(type: "bigint", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(
                    type: "timestamp with time zone",
                    nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey(
                    "PK_anonymous_team_contributions",
                    x => new { x.AnonymousId, x.Event });
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "anonymous_team_contributions");
    }
}

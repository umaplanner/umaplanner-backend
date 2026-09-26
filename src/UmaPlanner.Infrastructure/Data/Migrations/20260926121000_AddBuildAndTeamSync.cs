using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UmaPlanner.Infrastructure.Data;

#nullable disable

namespace UmaPlanner.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260926121000_AddBuildAndTeamSync")]
public partial class AddBuildAndTeamSync : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_uma_builds",
            columns: table => new
            {
                UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Event = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Data = table.Column<string>(type: "jsonb", nullable: false),
                DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_uma_builds", x => new { x.UserId, x.Event, x.Id });
                table.ForeignKey(
                    name: "FK_user_uma_builds_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "user_teams",
            columns: table => new
            {
                UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Event = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Data = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_teams", x => new { x.UserId, x.Event });
                table.ForeignKey(
                    name: "FK_user_teams_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_uma_builds");
        migrationBuilder.DropTable(name: "user_teams");
    }
}

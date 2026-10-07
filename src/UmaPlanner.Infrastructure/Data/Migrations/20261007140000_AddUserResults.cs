using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UmaPlanner.Infrastructure.Data;

#nullable disable

namespace UmaPlanner.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261007140000_AddUserResults")]
public partial class AddUserResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_results",
            columns: table => new
            {
                UserId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Event = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Data = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_results", x => new { x.UserId, x.Event });
                table.ForeignKey(
                    name: "FK_user_results_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_results");
    }
}

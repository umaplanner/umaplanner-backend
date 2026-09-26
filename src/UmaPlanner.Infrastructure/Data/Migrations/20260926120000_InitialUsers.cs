using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UmaPlanner.Infrastructure.Data;

#nullable disable

namespace UmaPlanner.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260926120000_InitialUsers")]
public partial class InitialUsers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS users (
                "Id" character varying(128) NOT NULL,
                "DiscordId" character varying(128) NOT NULL,
                "Username" character varying(128) NOT NULL,
                "AvatarUrl" character varying(512),
                "TrainerId" character varying(128),
                "CreatedAt" character varying(64) NOT NULL,
                CONSTRAINT "PK_users" PRIMARY KEY ("Id")
            );

            CREATE UNIQUE INDEX IF NOT EXISTS "IX_users_DiscordId"
                ON users ("DiscordId");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "users");
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UmaPlanner.Infrastructure.Data.Migrations;

public partial class AddBuildAndTeamSync : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF to_regclass('user_uma_builds') IS NULL THEN
                    CREATE TABLE user_uma_builds (
                        "UserId" character varying(128) NOT NULL,
                        "Event" character varying(128) NOT NULL,
                        "Id" character varying(128) NOT NULL,
                        "Data" jsonb NOT NULL,
                        CONSTRAINT "PK_user_uma_builds"
                            PRIMARY KEY ("UserId", "Event", "Id"),
                        CONSTRAINT "FK_user_uma_builds_users_UserId"
                            FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
                    );
                END IF;
            END $$;

            ALTER TABLE user_uma_builds
                ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone;

            CREATE TABLE IF NOT EXISTS user_teams (
                "UserId" character varying(128) NOT NULL,
                "Event" character varying(128) NOT NULL,
                "Data" jsonb NOT NULL,
                CONSTRAINT "PK_user_teams" PRIMARY KEY ("UserId", "Event"),
                CONSTRAINT "FK_user_teams_users_UserId"
                    FOREIGN KEY ("UserId") REFERENCES users ("Id") ON DELETE CASCADE
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_teams");
        migrationBuilder.Sql("""
            ALTER TABLE user_uma_builds
                DROP COLUMN IF EXISTS "DeletedAt";
            """);
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using UmaPlanner.Infrastructure.Data;

namespace UmaPlanner.Infrastructure.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261011100000_AddResultCountToAdminDailyStats")]
public partial class AddResultCountToAdminDailyStats : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "ResultCount",
            table: "admin_daily_stats",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);
        migrationBuilder.AddColumn<long>(
            name: "TicketCount",
            table: "admin_daily_stats",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.Sql(
            """
            WITH result_counts AS (
                SELECT "Event", COUNT(*)::bigint AS result_count
                FROM user_results
                GROUP BY "Event"
            ),
            ticket_counts AS (
                SELECT results."Event",
                       SUM((
                           SELECT COUNT(*)
                           FROM jsonb_object_keys(day_data.value)
                       ))::bigint AS ticket_count
                FROM user_results AS results
                CROSS JOIN LATERAL jsonb_each(
                    CASE
                        WHEN jsonb_typeof(results."Data") = 'object'
                            THEN results."Data"
                        ELSE '{}'::jsonb
                    END
                ) AS day_data(key, value)
                WHERE day_data.key ~ '^round[0-9]+Day[0-9]+$'
                  AND jsonb_typeof(day_data.value) = 'object'
                GROUP BY results."Event"
            )
            UPDATE admin_daily_stats AS stats
            SET "ResultCount" = CASE
                WHEN stats."Event" = '' THEN COALESCE(
                    (SELECT SUM(result_count) FROM result_counts),
                    0
                )
                ELSE COALESCE(
                    (SELECT result_count
                     FROM result_counts
                     WHERE result_counts."Event" = stats."Event"),
                    0
                )
            END,
            "TicketCount" = CASE
                WHEN stats."Event" = '' THEN COALESCE(
                    (SELECT SUM(ticket_count) FROM ticket_counts),
                    0
                )
                ELSE COALESCE(
                    (SELECT ticket_count
                     FROM ticket_counts
                     WHERE ticket_counts."Event" = stats."Event"),
                    0
                )
            END;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ResultCount",
            table: "admin_daily_stats");
        migrationBuilder.DropColumn(
            name: "TicketCount",
            table: "admin_daily_stats");
    }
}

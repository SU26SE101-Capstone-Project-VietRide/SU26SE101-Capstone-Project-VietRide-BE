using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddImmutableActivityLogReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "source_event_id",
                schema: "vietride_identity",
                table: "activity_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_activity_logs_created_at_id",
                schema: "vietride_identity",
                table: "activity_logs",
                columns: new[] { "created_at", "id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "uq_activity_logs_source_event_id",
                schema: "vietride_identity",
                table: "activity_logs",
                column: "source_event_id",
                unique: true,
                filter: "source_event_id IS NOT NULL");

            migrationBuilder.Sql(
                """
                CREATE OR REPLACE FUNCTION vietride_identity.reject_activity_log_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'activity_logs is append-only'
                        USING ERRCODE = '55000';
                END;
                $function$;

                CREATE TRIGGER trg_activity_logs_append_only
                BEFORE UPDATE OR DELETE ON vietride_identity.activity_logs
                FOR EACH ROW
                EXECUTE FUNCTION vietride_identity.reject_activity_log_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_activity_logs_append_only
                    ON vietride_identity.activity_logs;
                DROP FUNCTION IF EXISTS vietride_identity.reject_activity_log_mutation();
                """);

            migrationBuilder.DropIndex(
                name: "idx_activity_logs_created_at_id",
                schema: "vietride_identity",
                table: "activity_logs");

            migrationBuilder.DropIndex(
                name: "uq_activity_logs_source_event_id",
                schema: "vietride_identity",
                table: "activity_logs");

            migrationBuilder.DropColumn(
                name: "source_event_id",
                schema: "vietride_identity",
                table: "activity_logs");
        }
    }
}

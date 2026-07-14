using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTripAuditLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trip_audit_logs",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    metadata = table.Column<JsonElement>(type: "jsonb", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_audit_logs", x => x.id);
                    table.ForeignKey(
                        name: "FK_trip_audit_logs_trips_trip_id",
                        column: x => x.trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_trip_audit_logs_action_occurred",
                schema: "vietride_trip",
                table: "trip_audit_logs",
                columns: new[] { "action", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "idx_trip_audit_logs_actor_occurred",
                schema: "vietride_trip",
                table: "trip_audit_logs",
                columns: new[] { "actor_user_id", "occurred_at" },
                descending: new[] { false, true },
                filter: "actor_user_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_trip_audit_logs_trip_occurred",
                schema: "vietride_trip",
                table: "trip_audit_logs",
                columns: new[] { "trip_id", "occurred_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trip_audit_logs",
                schema: "vietride_trip");
        }
    }
}

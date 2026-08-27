using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Trip.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddShuttleAssignmentAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shuttle_trip_assignment_audit_logs",
                schema: "vietride_trip",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    shuttle_trip_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    metadata = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shuttle_trip_assignment_audit_logs", x => x.id);
                    table.CheckConstraint("chk_shuttle_trip_assignment_audit_logs_action", "action IN ('INITIAL_ASSIGNED', 'REASSIGNED')");
                    table.ForeignKey(
                        name: "fk_shuttle_assignment_audit_shuttle_trip",
                        column: x => x.shuttle_trip_id,
                        principalSchema: "vietride_trip",
                        principalTable: "shuttle_trips",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_shuttle_assignment_audit_operator_trip_occurred",
                schema: "vietride_trip",
                table: "shuttle_trip_assignment_audit_logs",
                columns: new[] { "operator_id", "shuttle_trip_id", "occurred_at" },
                descending: new[] { false, false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shuttle_trip_assignment_audit_logs",
                schema: "vietride_trip");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptionUsageWarningMarkers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "subscription_usage_warning_markers",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    period_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    row_version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_usage_warning_markers", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscription_usage_warning_markers_subscription_id",
                        column: x => x.subscription_id,
                        principalSchema: "vietride_identity",
                        principalTable: "operator_subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "uq_subscription_usage_warning_markers_period",
                schema: "vietride_identity",
                table: "subscription_usage_warning_markers",
                columns: new[] { "subscription_id", "resource", "period_key" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_subscription_usage_warning_markers_updated_at
                BEFORE UPDATE ON vietride_identity.subscription_usage_warning_markers
                FOR EACH ROW EXECUTE FUNCTION vietride_identity.trg_set_updated_at();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_usage_warning_markers",
                schema: "vietride_identity");
        }
    }
}

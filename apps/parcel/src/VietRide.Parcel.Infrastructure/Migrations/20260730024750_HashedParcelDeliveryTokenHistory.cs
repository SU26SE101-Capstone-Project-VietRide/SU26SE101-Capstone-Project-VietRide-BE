using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HashedParcelDeliveryTokenHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_delivery_tokens",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    issued_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issue_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_delivery_tokens", x => x.id);
                    table.CheckConstraint("chk_parcel_delivery_tokens_issue_reason", "issue_reason IN ('INITIAL_DELIVERY', 'RESEND', 'MIGRATION_BACKFILL')");
                    table.ForeignKey(
                        name: "fk_parcel_delivery_tokens_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_parcel_delivery_tokens_expires_at_active",
                schema: "vietride_parcel",
                table: "parcel_delivery_tokens",
                column: "expires_at",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_parcel_delivery_tokens_active_parcel",
                schema: "vietride_parcel",
                table: "parcel_delivery_tokens",
                column: "parcel_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_parcel_delivery_tokens_token_hash",
                schema: "vietride_parcel",
                table: "parcel_delivery_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.Sql(
                """
                INSERT INTO vietride_parcel.parcel_delivery_tokens
                    (id, parcel_id, token_hash, expires_at, revoked_at,
                     issued_by_user_id, issue_reason, created_at, updated_at)
                SELECT
                    gen_random_uuid(),
                    parcel.id,
                    encode(
                        digest(lower(parcel.delivery_token::text), 'sha256'),
                        'hex'),
                    COALESCE(
                        parcel.delivery_token_expires_at,
                        parcel.updated_at,
                        parcel.created_at,
                        now()),
                    parcel.delivery_token_revoked_at,
                    NULL,
                    'MIGRATION_BACKFILL',
                    COALESCE(parcel.updated_at, parcel.created_at, now()),
                    COALESCE(parcel.updated_at, parcel.created_at, now())
                FROM vietride_parcel.parcels parcel
                WHERE parcel.delivery_token IS NOT NULL;
                """);

            migrationBuilder.DropIndex(
                name: "uq_parcels_delivery_token",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "delivery_token",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "delivery_token_expires_at",
                schema: "vietride_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "delivery_token_revoked_at",
                schema: "vietride_parcel",
                table: "parcels");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "delivery_token",
                schema: "vietride_parcel",
                table: "parcels",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivery_token_expires_at",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "delivery_token_revoked_at",
                schema: "vietride_parcel",
                table: "parcels",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE vietride_parcel.parcels parcel
                SET delivery_token = replacement.raw_token,
                    delivery_token_expires_at = replacement.expires_at,
                    -- SHA-256 is irreversible. Down() restores the legacy columns
                    -- with replacement UUIDs that are deliberately invalidated so
                    -- no fabricated token can become an active delivery link.
                    delivery_token_revoked_at = COALESCE(replacement.revoked_at, now())
                FROM (
                    SELECT DISTINCT ON (token.parcel_id)
                        token.parcel_id,
                        gen_random_uuid() AS raw_token,
                        token.expires_at,
                        token.revoked_at
                    FROM vietride_parcel.parcel_delivery_tokens token
                    ORDER BY
                        token.parcel_id,
                        (token.revoked_at IS NULL) DESC,
                        token.created_at DESC,
                        token.id DESC
                ) replacement
                WHERE parcel.id = replacement.parcel_id;
                """);

            migrationBuilder.CreateIndex(
                name: "uq_parcels_delivery_token",
                schema: "vietride_parcel",
                table: "parcels",
                column: "delivery_token",
                unique: true,
                filter: "delivery_token IS NOT NULL");

            migrationBuilder.DropTable(
                name: "parcel_delivery_tokens",
                schema: "vietride_parcel");
        }
    }
}

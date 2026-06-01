using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VietRide.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitIdentityAuth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "vietride_identity");

            migrationBuilder.CreateTable(
                name: "operators",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_operators", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    password_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    display_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    avatar_url = table.Column<string>(type: "text", nullable: true),
                    role = table.Column<string>(type: "user_role", nullable: false),
                    status = table.Column<string>(type: "user_status", nullable: false),
                    operator_id = table.Column<Guid>(type: "uuid", nullable: true),
                    failed_login_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_failed_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("chk_users_operator_role", "(role IN ('DRIVER', 'ASSISTANT', 'OPERATOR_STAFF', 'OPERATOR_ADMIN') AND operator_id IS NOT NULL) OR (role IN ('PASSENGER', 'SYSTEM_ADMIN') AND operator_id IS NULL)");
                    table.CheckConstraint("chk_users_phone_format", "phone IS NULL OR phone ~ '^\\+84[0-9]{9,10}$'");
                    table.ForeignKey(
                        name: "fk_users_operator_id",
                        column: x => x.operator_id,
                        principalSchema: "vietride_identity",
                        principalTable: "operators",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "email_verification_purpose", nullable: false),
                    code = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_verification_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_email_verification_tokens_user_id",
                        column: x => x.user_id,
                        principalSchema: "vietride_identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oauth_identities",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "oauth_provider", nullable: false),
                    provider_subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    provider_email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    linked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_oauth_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_oauth_identities_user_id",
                        column: x => x.user_id,
                        principalSchema: "vietride_identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "refresh_token_revoke_reason", nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_parent_token_id",
                        column: x => x.parent_token_id,
                        principalSchema: "vietride_identity",
                        principalTable: "refresh_tokens",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_user_id",
                        column: x => x.user_id,
                        principalSchema: "vietride_identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_devices",
                schema: "vietride_identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fcm_token = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    platform = table.Column<string>(type: "device_platform", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    last_active_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_devices", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_devices_user_id",
                        column: x => x.user_id,
                        principalSchema: "vietride_identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_email_verification_tokens_expires_at",
                schema: "vietride_identity",
                table: "email_verification_tokens",
                column: "expires_at",
                filter: "used_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_email_verification_tokens_user_purpose",
                schema: "vietride_identity",
                table: "email_verification_tokens",
                columns: new[] { "user_id", "purpose" },
                filter: "used_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "uq_email_verification_tokens_code_purpose",
                schema: "vietride_identity",
                table: "email_verification_tokens",
                columns: new[] { "code", "purpose" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_oauth_identities_user_id",
                schema: "vietride_identity",
                table: "oauth_identities",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_oauth_identities_provider_subject",
                schema: "vietride_identity",
                table: "oauth_identities",
                columns: new[] { "provider", "provider_subject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_oauth_identities_user_provider",
                schema: "vietride_identity",
                table: "oauth_identities",
                columns: new[] { "user_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_processed_at_occurred_at",
                schema: "vietride_identity",
                table: "outbox_messages",
                columns: new[] { "processed_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "idx_refresh_tokens_expires_at",
                schema: "vietride_identity",
                table: "refresh_tokens",
                column: "expires_at",
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "idx_refresh_tokens_family_id",
                schema: "vietride_identity",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "idx_refresh_tokens_parent_token_id",
                schema: "vietride_identity",
                table: "refresh_tokens",
                column: "parent_token_id",
                filter: "parent_token_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_refresh_tokens_user_id",
                schema: "vietride_identity",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uq_refresh_tokens_token_hash",
                schema: "vietride_identity",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_user_devices_fcm_token",
                schema: "vietride_identity",
                table: "user_devices",
                column: "fcm_token",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_user_devices_last_active_at",
                schema: "vietride_identity",
                table: "user_devices",
                column: "last_active_at",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "idx_user_devices_user_active",
                schema: "vietride_identity",
                table: "user_devices",
                column: "user_id",
                filter: "is_active = TRUE");

            migrationBuilder.CreateIndex(
                name: "uq_user_devices_user_fcm_token",
                schema: "vietride_identity",
                table: "user_devices",
                columns: new[] { "user_id", "fcm_token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "idx_users_operator_id",
                schema: "vietride_identity",
                table: "users",
                column: "operator_id",
                filter: "operator_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "idx_users_role_status",
                schema: "vietride_identity",
                table: "users",
                columns: new[] { "role", "status" });

            // Functional/expression unique index: LOWER(email) WHERE deleted_at IS NULL.
            // EF Core does not support expression indexes natively; raw DDL is required.
            // Schema ref: db-schema/identity-user/schema.sql lines 171-173.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX uq_users_email ON vietride_identity.users (LOWER(email)) WHERE deleted_at IS NULL;");

            migrationBuilder.CreateIndex(
                name: "uq_users_phone",
                schema: "vietride_identity",
                table: "users",
                column: "phone",
                unique: true,
                filter: "deleted_at IS NULL AND phone IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_verification_tokens",
                schema: "vietride_identity");

            migrationBuilder.DropTable(
                name: "oauth_identities",
                schema: "vietride_identity");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "vietride_identity");

            migrationBuilder.DropTable(
                name: "refresh_tokens",
                schema: "vietride_identity");

            migrationBuilder.DropTable(
                name: "user_devices",
                schema: "vietride_identity");

            migrationBuilder.DropTable(
                name: "users",
                schema: "vietride_identity");

            migrationBuilder.DropTable(
                name: "operators",
                schema: "vietride_identity");
        }
    }
}

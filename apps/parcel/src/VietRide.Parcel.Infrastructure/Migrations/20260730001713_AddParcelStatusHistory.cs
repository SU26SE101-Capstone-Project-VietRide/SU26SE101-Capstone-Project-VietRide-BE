using Microsoft.EntityFrameworkCore.Migrations;

using System;

#nullable disable

namespace VietRide.Parcel.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_status_history",
                schema: "vietride_parcel",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "vietride_parcel.parcel_status", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_status_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_status_history_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalSchema: "vietride_parcel",
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "idx_parcel_status_history_parcel_occurred_id",
                schema: "vietride_parcel",
                table: "parcel_status_history",
                columns: new[] { "parcel_id", "occurred_at", "id" });

            migrationBuilder.CreateIndex(
                name: "uq_parcel_status_history_migration_baseline",
                schema: "vietride_parcel",
                table: "parcel_status_history",
                column: "parcel_id",
                unique: true,
                filter: "source = 'MIGRATION_BASELINE'");

            migrationBuilder.Sql(
                """
                LOCK TABLE vietride_parcel.parcels IN SHARE ROW EXCLUSIVE MODE;

                INSERT INTO vietride_parcel.parcel_status_history
                    (parcel_id, status, occurred_at, actor_type, actor_id, source, reason)
                SELECT id, status, statement_timestamp(), 'SYSTEM', NULL, 'MIGRATION_BASELINE', NULL
                FROM vietride_parcel.parcels;

                CREATE FUNCTION vietride_parcel.capture_parcel_status_history()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    history_actor_id UUID;
                    history_actor_type VARCHAR(20);
                    history_reason TEXT;
                BEGIN
                    IF OLD.status IS NOT DISTINCT FROM NEW.status THEN
                        RETURN NEW;
                    END IF;

                    history_actor_id := CASE
                        WHEN OLD.status = 'PENDING_OPERATOR_REVIEW'::vietride_parcel.parcel_status
                             AND NEW.reviewed_by_user_id IS DISTINCT FROM OLD.reviewed_by_user_id
                            THEN NEW.reviewed_by_user_id
                        WHEN NEW.status = 'CHECKED_IN'::vietride_parcel.parcel_status
                             AND NEW.checked_in_by_user_id IS DISTINCT FROM OLD.checked_in_by_user_id
                            THEN NEW.checked_in_by_user_id
                        WHEN OLD.status = 'CHECKED_IN'::vietride_parcel.parcel_status
                             AND NEW.reweighed_by_user_id IS DISTINCT FROM OLD.reweighed_by_user_id
                            THEN NEW.reweighed_by_user_id
                        WHEN OLD.status = 'READY_TO_LOAD'::vietride_parcel.parcel_status
                             AND NEW.status = 'LOADED'::vietride_parcel.parcel_status
                             AND NEW.loaded_by_user_id IS DISTINCT FROM OLD.loaded_by_user_id
                            THEN NEW.loaded_by_user_id
                        WHEN OLD.status = 'PENDING_TRANSFER_CONFIRM'::vietride_parcel.parcel_status
                             AND NEW.status = 'LOADED'::vietride_parcel.parcel_status
                             AND NEW.transfer_confirmed_by_user_id IS DISTINCT FROM OLD.transfer_confirmed_by_user_id
                            THEN NEW.transfer_confirmed_by_user_id
                        WHEN NEW.status = 'RETURNED'::vietride_parcel.parcel_status
                             AND NEW.returned_by_user_id IS DISTINCT FROM OLD.returned_by_user_id
                            THEN NEW.returned_by_user_id
                        WHEN NEW.status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
                             AND NEW.confirmed_by_user_id IS DISTINCT FROM OLD.confirmed_by_user_id
                            THEN NEW.confirmed_by_user_id
                        ELSE NULL
                    END;

                    history_actor_type := CASE
                        WHEN history_actor_id IS NOT NULL THEN 'USER'
                        WHEN OLD.status = 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status
                             AND NEW.status = 'DELIVERY_CONFIRMED'::vietride_parcel.parcel_status
                             AND NEW.confirmed_by_user_id IS NULL
                             AND NEW.confirmed_by_ip IS NOT NULL
                            THEN 'RECIPIENT'
                        WHEN OLD.status = 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status
                             AND NEW.status = 'DELIVERY_REJECTED'::vietride_parcel.parcel_status
                            THEN 'RECIPIENT'
                        WHEN OLD.status = 'DELIVERY_REJECTED'::vietride_parcel.parcel_status
                             AND NEW.status = 'DELIVERED_PENDING_CONFIRM'::vietride_parcel.parcel_status
                            THEN 'RECIPIENT'
                        ELSE 'UNKNOWN'
                    END;

                    history_reason := CASE NEW.status
                        WHEN 'PENDING_OPERATOR_ACTION'::vietride_parcel.parcel_status
                            THEN NEW.pending_action_reason
                        WHEN 'CANCELLED'::vietride_parcel.parcel_status
                            THEN NEW.cancellation_reason
                        WHEN 'REJECTED'::vietride_parcel.parcel_status
                            THEN NEW.rejection_reason
                        WHEN 'DELIVERY_REJECTED'::vietride_parcel.parcel_status
                            THEN NEW.rejection_reason
                        WHEN 'RETURNED'::vietride_parcel.parcel_status
                            THEN NEW.return_reason
                        ELSE NULL
                    END;

                    INSERT INTO vietride_parcel.parcel_status_history
                        (parcel_id, status, occurred_at, actor_type, actor_id, source, reason)
                    VALUES
                        (NEW.id, NEW.status, clock_timestamp(), history_actor_type,
                         history_actor_id, 'STATUS_TRIGGER', history_reason);

                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER trg_parcels_status_history
                    AFTER UPDATE OF status ON vietride_parcel.parcels
                    FOR EACH ROW
                    WHEN (OLD.status IS DISTINCT FROM NEW.status)
                    EXECUTE FUNCTION vietride_parcel.capture_parcel_status_history();

                CREATE FUNCTION vietride_parcel.reject_parcel_status_history_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'parcel_status_history is immutable'
                        USING ERRCODE = '55000';
                END;
                $function$;

                CREATE TRIGGER trg_parcel_status_history_immutable
                    BEFORE UPDATE OR DELETE ON vietride_parcel.parcel_status_history
                    FOR EACH ROW
                    EXECUTE FUNCTION vietride_parcel.reject_parcel_status_history_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_parcels_status_history ON vietride_parcel.parcels;
                DROP FUNCTION IF EXISTS vietride_parcel.capture_parcel_status_history();

                DROP TRIGGER IF EXISTS trg_parcel_status_history_immutable
                    ON vietride_parcel.parcel_status_history;
                DROP FUNCTION IF EXISTS vietride_parcel.reject_parcel_status_history_mutation();
                """);

            migrationBuilder.DropTable(
                name: "parcel_status_history",
                schema: "vietride_parcel");
        }
    }
}

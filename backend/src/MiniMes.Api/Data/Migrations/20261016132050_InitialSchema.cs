using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace MiniMes.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ncr_numbers");

            migrationBuilder.CreateSequence(
                name: "work_order_numbers",
                startValue: 1001L);

            migrationBuilder.CreateTable(
                name: "outbox",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "parts",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_number = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    refurb_interval_flights = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "part_revisions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_id = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<string>(type: "text", nullable: false),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_part_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_part_revisions_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "routings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    part_revision_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routings", x => x.id);
                    table.ForeignKey(
                        name: "fk_routings_part_revisions_part_revision_id",
                        column: x => x.part_revision_id,
                        principalTable: "part_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "serials",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    serial_number = table.Column<string>(type: "text", nullable: false),
                    part_revision_id = table.Column<int>(type: "integer", nullable: false),
                    lot_number = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_serials", x => x.id);
                    table.ForeignKey(
                        name: "fk_serials_part_revisions_part_revision_id",
                        column: x => x.part_revision_id,
                        principalTable: "part_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "routing_operations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    routing_id = table.Column<int>(type: "integer", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routing_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_routing_operations_routings_routing_id",
                        column: x => x.routing_id,
                        principalTable: "routings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_routing_operations_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "flights",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<int>(type: "integer", nullable: false),
                    flight_number = table.Column<int>(type: "integer", nullable: false),
                    mission = table.Column<string>(type: "text", nullable: false),
                    flown_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flights", x => x.id);
                    table.ForeignKey(
                        name: "fk_flights_serials_vehicle_id",
                        column: x => x.vehicle_id,
                        principalTable: "serials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "installations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    parent_id = table.Column<int>(type: "integer", nullable: false),
                    child_id = table.Column<int>(type: "integer", nullable: false),
                    installed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    installed_by = table.Column<string>(type: "text", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    removed_by = table.Column<string>(type: "text", nullable: true),
                    removal_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_installations", x => x.id);
                    table.ForeignKey(
                        name: "fk_installations_serials_child_id",
                        column: x => x.child_id,
                        principalTable: "serials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_installations_serials_parent_id",
                        column: x => x.parent_id,
                        principalTable: "serials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    serial_id = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hold_reason = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_orders_serials_serial_id",
                        column: x => x.serial_id,
                        principalTable: "serials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "flight_manifest",
                columns: table => new
                {
                    flight_id = table.Column<int>(type: "integer", nullable: false),
                    serial_id = table.Column<int>(type: "integer", nullable: false),
                    parent_serial_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_flight_manifest", x => new { x.flight_id, x.serial_id });
                    table.ForeignKey(
                        name: "fk_flight_manifest_flights_flight_id",
                        column: x => x.flight_id,
                        principalTable: "flights",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_flight_manifest_serials_serial_id",
                        column: x => x.serial_id,
                        principalTable: "serials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_order_operations",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_order_id = table.Column<int>(type: "integer", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    station_id = table.Column<int>(type: "integer", nullable: false),
                    is_rework = table.Column<bool>(type: "boolean", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_order_operations_stations_station_id",
                        column: x => x.station_id,
                        principalTable: "stations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_operations_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ncrs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    number = table.Column<string>(type: "text", nullable: false),
                    serial_id = table.Column<int>(type: "integer", nullable: false),
                    work_order_id = table.Column<int>(type: "integer", nullable: true),
                    work_order_operation_id = table.Column<int>(type: "integer", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    raised_by = table.Column<string>(type: "text", nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    disposition = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    dispositioned_by = table.Column<string>(type: "text", nullable: true),
                    dispositioned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    disposition_notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ncrs", x => x.id);
                    table.ForeignKey(
                        name: "fk_ncrs_serials_serial_id",
                        column: x => x.serial_id,
                        principalTable: "serials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ncrs_work_order_operations_work_order_operation_id",
                        column: x => x.work_order_operation_id,
                        principalTable: "work_order_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ncrs_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sign_offs",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    work_order_operation_id = table.Column<int>(type: "integer", nullable: false),
                    signed_by = table.Column<string>(type: "text", nullable: false),
                    signed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sign_offs", x => x.id);
                    table.ForeignKey(
                        name: "fk_sign_offs_work_order_operations_work_order_operation_id",
                        column: x => x.work_order_operation_id,
                        principalTable: "work_order_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_flight_manifest_serial_id",
                table: "flight_manifest",
                column: "serial_id");

            migrationBuilder.CreateIndex(
                name: "ix_flights_vehicle_id_flight_number",
                table: "flights",
                columns: new[] { "vehicle_id", "flight_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_installations_child_id",
                table: "installations",
                column: "child_id",
                unique: true,
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_installations_parent_id",
                table: "installations",
                column: "parent_id",
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ncrs_number",
                table: "ncrs",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ncrs_serial_id",
                table: "ncrs",
                column: "serial_id");

            migrationBuilder.CreateIndex(
                name: "ix_ncrs_status",
                table: "ncrs",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_ncrs_work_order_id",
                table: "ncrs",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_ncrs_work_order_operation_id",
                table: "ncrs",
                column: "work_order_operation_id");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_unpublished",
                table: "outbox",
                column: "id",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_part_revisions_part_id_revision",
                table: "part_revisions",
                columns: new[] { "part_id", "revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parts_part_number",
                table: "parts",
                column: "part_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_routing_operations_routing_id",
                table: "routing_operations",
                column: "routing_id");

            migrationBuilder.CreateIndex(
                name: "ix_routing_operations_station_id",
                table: "routing_operations",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_routings_part_revision_id_type",
                table: "routings",
                columns: new[] { "part_revision_id", "type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_serials_lot_number",
                table: "serials",
                column: "lot_number");

            migrationBuilder.CreateIndex(
                name: "ix_serials_part_revision_id",
                table: "serials",
                column: "part_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_serials_serial_number",
                table: "serials",
                column: "serial_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sign_offs_work_order_operation_id",
                table: "sign_offs",
                column: "work_order_operation_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stations_code",
                table: "stations",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_order_operations_station_id",
                table: "work_order_operations",
                column: "station_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_operations_work_order_id",
                table: "work_order_operations",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_number",
                table: "work_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_serial_id_type_status",
                table: "work_orders",
                columns: new[] { "serial_id", "type", "status" });

            // Audit trail: sign-offs and flight records are append-only. The DbContext refuses to update or
            // delete them; this trigger makes the database refuse too, whatever client is connected.
            migrationBuilder.Sql("""
                CREATE FUNCTION reject_audit_mutation() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION '% on % rejected: table is append-only', TG_OP, TG_TABLE_NAME
                        USING ERRCODE = 'restrict_violation';
                END $$;

                CREATE TRIGGER sign_offs_append_only BEFORE UPDATE OR DELETE ON sign_offs
                    FOR EACH ROW EXECUTE FUNCTION reject_audit_mutation();
                CREATE TRIGGER flights_append_only BEFORE UPDATE OR DELETE ON flights
                    FOR EACH ROW EXECUTE FUNCTION reject_audit_mutation();
                CREATE TRIGGER flight_manifest_append_only BEFORE UPDATE OR DELETE ON flight_manifest
                    FOR EACH ROW EXECUTE FUNCTION reject_audit_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER flight_manifest_append_only ON flight_manifest;
                DROP TRIGGER flights_append_only ON flights;
                DROP TRIGGER sign_offs_append_only ON sign_offs;
                DROP FUNCTION reject_audit_mutation();
                """);

            migrationBuilder.DropTable(
                name: "flight_manifest");

            migrationBuilder.DropTable(
                name: "installations");

            migrationBuilder.DropTable(
                name: "ncrs");

            migrationBuilder.DropTable(
                name: "outbox");

            migrationBuilder.DropTable(
                name: "routing_operations");

            migrationBuilder.DropTable(
                name: "sign_offs");

            migrationBuilder.DropTable(
                name: "flights");

            migrationBuilder.DropTable(
                name: "routings");

            migrationBuilder.DropTable(
                name: "work_order_operations");

            migrationBuilder.DropTable(
                name: "stations");

            migrationBuilder.DropTable(
                name: "work_orders");

            migrationBuilder.DropTable(
                name: "serials");

            migrationBuilder.DropTable(
                name: "part_revisions");

            migrationBuilder.DropTable(
                name: "parts");

            migrationBuilder.DropSequence(
                name: "ncr_numbers");

            migrationBuilder.DropSequence(
                name: "work_order_numbers");
        }
    }
}

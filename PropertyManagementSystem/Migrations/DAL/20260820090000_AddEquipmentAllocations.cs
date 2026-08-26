using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PropertyManagementSystem.Data;

#nullable disable

namespace PropertyManagementSystem.Migrations.DAL
{
    [DbContext(typeof(PropertyManagementSystem.Data.DAL))]
    [Migration("20260820090000_AddEquipmentAllocations")]
    public sealed class AddEquipmentAllocations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Some deployed legacy databases use identity columns without a PK or
            // unique constraint. Add candidate keys only when one is missing so
            // the allocation foreign keys can be created safely.
            migrationBuilder.Sql(@"
IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID(N'dbo.GFMS_Equipments')
      AND i.is_unique = 1
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND ic.column_id = COLUMNPROPERTY(i.object_id, N'Id', 'ColumnId'))
)
    CREATE UNIQUE INDEX UX_GFMS_Equipments_Id ON dbo.GFMS_Equipments(Id);

IF NOT EXISTS
(
    SELECT 1
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID(N'dbo.GFMS_ReservationDetails')
      AND i.is_unique = 1
      AND (SELECT COUNT(*) FROM sys.index_columns ic
           WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.key_ordinal > 0) = 1
      AND EXISTS (SELECT 1 FROM sys.index_columns ic
                  WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id
                    AND ic.key_ordinal = 1 AND ic.column_id = COLUMNPROPERTY(i.object_id, N'ReservationDetailId', 'ColumnId'))
)
    CREATE UNIQUE INDEX UX_GFMS_ReservationDetails_Id
        ON dbo.GFMS_ReservationDetails(ReservationDetailId);");

            migrationBuilder.CreateTable(
                name: "GFMS_FacilityEquipmentLinks",
                columns: table => new
                {
                    FacilityEquipmentLinkId = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FacilityId = table.Column<int>(nullable: false),
                    EquipmentId = table.Column<int>(nullable: false),
                    RoomId = table.Column<int>(nullable: true),
                    DefaultQuantity = table.Column<int>(nullable: false, defaultValue: 1),
                    LinkType = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "INCLUDED"),
                    IsFixed = table.Column<bool>(nullable: false),
                    IsActive = table.Column<bool>(nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GFMS_FacilityEquipmentLinks", x => x.FacilityEquipmentLinkId);
                    table.CheckConstraint("CK_GFMS_FacilityEquipmentLinks_Quantity", "DefaultQuantity > 0");
                    table.CheckConstraint("CK_GFMS_FacilityEquipmentLinks_LinkType", "LinkType IN ('INCLUDED', 'ADD_ON')");
                    table.ForeignKey("FK_GFMS_FacilityEquipmentLinks_GFMS_Facility_FacilityId", x => x.FacilityId,
                        "GFMS_Facility", "FacilityId", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_GFMS_FacilityEquipmentLinks_GFMS_Equipments_EquipmentId", x => x.EquipmentId,
                        "GFMS_Equipments", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_GFMS_FacilityEquipmentLinks_GFMS_Rooms_RoomId", x => x.RoomId,
                        "GFMS_Rooms", "RoomId", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GFMS_ReservationEquipments",
                columns: table => new
                {
                    ReservationEquipmentId = table.Column<int>(nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationDetailId = table.Column<int>(nullable: false),
                    EquipmentId = table.Column<int>(nullable: false),
                    Quantity = table.Column<int>(nullable: false, defaultValue: 1),
                    SourceType = table.Column<string>(maxLength: 20, nullable: false),
                    SourceFacilityId = table.Column<int>(nullable: true),
                    SourceRoomId = table.Column<int>(nullable: true),
                    IsAutomatic = table.Column<bool>(nullable: false),
                    AllocationStatus = table.Column<string>(maxLength: 20, nullable: false, defaultValue: "RESERVED"),
                    AssignedCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EquipmentNameSnapshot = table.Column<string>(maxLength: 200, nullable: false),
                    DateAssigned = table.Column<DateTime>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GFMS_ReservationEquipments", x => x.ReservationEquipmentId);
                    table.CheckConstraint("CK_GFMS_ReservationEquipments_Quantity", "Quantity > 0");
                    table.ForeignKey("FK_GFMS_ReservationEquipments_GFMS_ReservationDetails_ReservationDetailId",
                        x => x.ReservationDetailId, "GFMS_ReservationDetails", "ReservationDetailId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_GFMS_ReservationEquipments_GFMS_Equipments_EquipmentId",
                        x => x.EquipmentId, "GFMS_Equipments", "Id", onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_GFMS_FacilityEquipmentLinks_LocationEquipment",
                table: "GFMS_FacilityEquipmentLinks",
                columns: new[] { "FacilityId", "EquipmentId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_GFMS_ReservationEquipments_DetailEquipment",
                table: "GFMS_ReservationEquipments",
                columns: new[] { "ReservationDetailId", "EquipmentId" },
                unique: true);

            migrationBuilder.Sql(@"
INSERT INTO dbo.GFMS_FacilityEquipmentLinks
    (FacilityId, EquipmentId, RoomId, DefaultQuantity, LinkType, IsFixed, IsActive)
SELECT e.FacilityId, e.Id, e.RoomId,
       CASE WHEN ISNULL(e.EquipmentQty, 0) < 1 THEN 1 ELSE e.EquipmentQty END,
       'INCLUDED', CASE WHEN e.RoomId IS NULL THEN 0 ELSE 1 END, 1
FROM dbo.GFMS_Equipments e
WHERE e.FacilityId IS NOT NULL
  AND ISNULL(e.EquipmentQty, 0) > 0
  AND EXISTS (SELECT 1 FROM dbo.GFMS_Facility f WHERE f.FacilityId = e.FacilityId)
  AND (e.RoomId IS NULL OR EXISTS
       (SELECT 1 FROM dbo.GFMS_Rooms r
        WHERE r.RoomId = e.RoomId AND r.FacilityId = e.FacilityId));");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "GFMS_ReservationEquipments");
            migrationBuilder.DropTable(name: "GFMS_FacilityEquipmentLinks");
            migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GFMS_Equipments') AND name = N'UX_GFMS_Equipments_Id')
    DROP INDEX UX_GFMS_Equipments_Id ON dbo.GFMS_Equipments;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'dbo.GFMS_ReservationDetails') AND name = N'UX_GFMS_ReservationDetails_Id')
    DROP INDEX UX_GFMS_ReservationDetails_Id ON dbo.GFMS_ReservationDetails;");
        }
    }
}

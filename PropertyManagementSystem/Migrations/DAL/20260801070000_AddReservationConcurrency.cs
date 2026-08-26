using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PropertyManagementSystem.Data;

#nullable disable

namespace PropertyManagementSystem.Migrations.DAL
{
    [DbContext(typeof(PropertyManagementSystem.Data.DAL))]
    [Migration("20260801070000_AddReservationConcurrency")]
    public sealed class AddReservationConcurrency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "GFMS_ReservationHeader",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "GFMS_ReservationDetails",
                type: "rowversion",
                rowVersion: true,
                nullable: false);

            migrationBuilder.CreateIndex(
                name: "UX_GFMS_ReservationHeader_ReservationCode",
                table: "GFMS_ReservationHeader",
                column: "ReservationCode",
                unique: true,
                filter: "[ReservationCode] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GFMS_ReservationDetails_Room_Slot",
                table: "GFMS_ReservationDetails",
                columns: new[] { "RoomId", "StartDate", "EndDate" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_GFMS_ReservationHeader_ReservationCode",
                table: "GFMS_ReservationHeader");
            migrationBuilder.DropIndex(
                name: "IX_GFMS_ReservationDetails_Room_Slot",
                table: "GFMS_ReservationDetails");
            migrationBuilder.DropColumn(name: "RowVersion", table: "GFMS_ReservationHeader");
            migrationBuilder.DropColumn(name: "RowVersion", table: "GFMS_ReservationDetails");
        }
    }
}

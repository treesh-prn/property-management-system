using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PropertyManagementSystem.Services;
using Xunit;

namespace PropertyManagementSystem.Tests;

public class OccupancyReportSqlServerTests
{
    [Theory]
    [InlineData("FN", "Requestor", "Function Room")]
    [InlineData("GH", "Guest", "Guesthouse")]
    public async Task Procedure_and_service_return_booking_identity_and_overlapping_approved_stays(string type, string occupant, string label)
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithDatabase(async (connection, service) =>
        {
            await Execute(connection, @"
INSERT INTO GFMS_Facility VALUES (1, 'Facility');
INSERT INTO GFMS_Rooms VALUES (1, 'Room');
INSERT INTO GFMS_ReservationHeader VALUES (1, 'RSQ-FN-TEST', 'Requestor', 'FN', 'APPROVED'),
 (2, 'RSQ-GH-TEST', 'Requestor', 'GH', 'APPROVED'), (3, 'DRAFT', 'Requestor', 'FN', 'DRAFT');
INSERT INTO GFMS_ReservationDetails VALUES
 (1, 1, 1, 'Guest', '2026-11-05T12:00:00', '2026-11-13T12:00:00'),
 (2, 1, 1, 'Guest', '2026-11-12T18:00:00', '2026-11-13T12:00:00'),
 (3, 1, 1, 'Guest', '2026-11-07T12:00:00', '2026-11-08T12:00:00');");
            var rows = await service.GetOccupancyReportAsync(new(2026, 11, 6), new(2026, 11, 12), type);
            var row = Assert.Single(rows);
            Assert.Equal($"RSQ-{type}-TEST", row.ReservationCode);
            Assert.Equal("Requestor", row.Requestor);
            Assert.Equal(occupant, row.Occupant);
            Assert.Equal(label, row.ReservationType);
            Assert.Equal("Facility", row.FacilityName);
            Assert.Equal("Room", row.RoomName);
            Assert.True(new OccupancyPdfService().Generate(rows, new(2026, 11, 6), new(2026, 11, 12), type).Length > 0);
            Assert.True(new ExcelReportService().GenerateOccupancy(rows, new(2026, 11, 6), new(2026, 11, 12), type).Length > 0);
            await using var command = new SqlCommand("EXEC dbo.GFMS_GetReservationOccupancy '2026-11-06', '2026-11-12', 'FN'", connection);
            await using var reader = await command.ExecuteReaderAsync();
            foreach (var legacy in new[] { "Start Date", "End Date", "Start Time", "End Time", "Reservation Type", "Facility/Guesthouse", "Room Name", "Date of Stay", "No of Days" })
                Assert.True(reader.GetOrdinal(legacy) >= 0);
        });
    }

    [Fact]
    public async Task Empty_results_work_and_outdated_procedure_produces_actionable_error()
    {
        if (!OperatingSystem.IsWindows()) return;
        await WithDatabase(async (connection, service) =>
        {
            Assert.Empty(await service.GetOccupancyReportAsync(new(2026, 11, 6), new(2026, 11, 12), "FN"));
            await Execute(connection, "ALTER PROCEDURE dbo.GFMS_GetReservationOccupancy @StartDate date, @EndDate date, @ReservationType nvarchar(50) AS SELECT CAST(NULL AS datetime) AS [Start Date] WHERE 1=0;");
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetOccupancyReportAsync(new(2026, 11, 6), new(2026, 11, 12), "FN"));
            Assert.Contains("ReservationCode", error.Message);
            Assert.Contains("outdated", error.Message);
        });
    }

    private static async Task WithDatabase(Func<SqlConnection, ReportServices, Task> check)
    {
        var database = "PmsOccupancyTest_" + Guid.NewGuid().ToString("N");
        var builder = new SqlConnectionStringBuilder { DataSource = @"(localdb)\MSSQLLocalDB", InitialCatalog = "master", IntegratedSecurity = true, TrustServerCertificate = true };
        await using var master = new SqlConnection(builder.ConnectionString);
        await master.OpenAsync();
        await Execute(master, $"CREATE DATABASE [{database}]");
        try
        {
            builder.InitialCatalog = database;
            await using var connection = new SqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await Execute(connection, @"
CREATE TABLE GFMS_Facility (FacilityId int, FacilityName nvarchar(100));
CREATE TABLE GFMS_Rooms (RoomId int, RoomName nvarchar(100));
CREATE TABLE GFMS_ReservationHeader (ReservationId int, ReservationCode nvarchar(100), Requestor nvarchar(100), ReservationType nvarchar(50), Status nvarchar(100));
CREATE TABLE GFMS_ReservationDetails (ReservationId int, FacilityId int, RoomId int, GuestName nvarchar(100), StartDate datetime, EndDate datetime);");
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PropertyManagementSystem", "Data", "Scripts", "CorrectReportProcedures.sql"))) directory = directory.Parent;
            Assert.NotNull(directory);
            var script = await File.ReadAllTextAsync(Path.Combine(directory!.FullName, "PropertyManagementSystem", "Data", "Scripts", "CorrectReportProcedures.sql"));
            var procedure = script[script.IndexOf("CREATE OR ALTER PROCEDURE [dbo].[GFMS_GetReservationOccupancy]", StringComparison.Ordinal)..];
            procedure = procedure[..procedure.LastIndexOf("GO", StringComparison.Ordinal)];
            await Execute(connection, procedure);
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:ApplicationDbContextConnection"] = builder.ConnectionString }).Build();
            await check(connection, new ReportServices(configuration));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await Execute(master, $"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]");
        }
    }

    private static async Task Execute(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

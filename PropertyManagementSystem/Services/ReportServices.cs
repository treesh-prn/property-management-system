using Microsoft.Data.SqlClient;
using PropertyManagementSystem.Models;
using System.Data;

namespace PropertyManagementSystem.Services
{
    public class ReportServices
    {
        private readonly string _connectionString;

        public ReportServices(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("ApplicationDbContextConnection")
                ?? throw new InvalidOperationException("The report database connection is not configured.");
        }

        public async Task<List<ReservationReport>> GetCompanyChargesReportAsync(
            DateTime startDate,
            DateTime endDate,
            string preparedBy,
            CancellationToken cancellationToken = default)
        {
            var reportData = new List<ReservationReport>();

            await using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using (var command = new SqlCommand("dbo.GFMS_GenerateCompanyChargesReport", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    command.CommandTimeout = 120;

                    command.Parameters.Add("@StartDate", SqlDbType.DateTime2).Value = startDate.Date;
                    command.Parameters.Add("@EndDate", SqlDbType.DateTime2).Value = endDate.Date;
                    command.Parameters.Add("@PreparedBy", SqlDbType.NVarChar, 100).Value = preparedBy;

                    await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await reader.ReadAsync(cancellationToken))
                        {
                            var data = new ReservationReport
                            {
                                CompanyCode = ReadString(reader, "CompanyCode"),
                                Department = ReadString(reader, "Department"),
                                RoomUsed = ReadString(reader, "RoomUsed"),
                                Dates = ReadString(reader, "Dates"),
                                NoOfDays = ReadInt32(reader, "NoOfDays"),
                                EquipmentUsed = ReadString(reader, "EquipmentUsed"),
                                RoomCharge = ReadDecimal(reader, "RoomCharge"),
                                EquipmentCharge = ReadDecimal(reader, "EquipmentCharge"),
                                TotalCharge = ReadDecimal(reader, "TotalCharge"),
                                PreparedBy = ReadString(reader, "PreparedBy"),
                                NotedBy = ReadString(reader, "NotedBy")
                            };
                            reportData.Add(data);
                        }
                    }
                }
            }

            return reportData;
        }

        public async Task<List<OccupancyReportRow>> GetOccupancyReportAsync(
            DateTime startDate,
            DateTime endDate,
            string reservationType,
            CancellationToken cancellationToken = default)
        {
            var reportData = new List<OccupancyReportRow>();
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new SqlCommand("dbo.GFMS_GetReservationOccupancy", connection)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 120
            };
            command.Parameters.Add("@StartDate", SqlDbType.Date).Value = startDate.Date;
            command.Parameters.Add("@EndDate", SqlDbType.Date).Value = endDate.Date;
            command.Parameters.Add("@ReservationType", SqlDbType.NVarChar, 50).Value = reservationType;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                reportData.Add(new OccupancyReportRow
                {
                    ReservationCode = ReadString(reader, "ReservationCode"),
                    Requestor = ReadString(reader, "Requestor"),
                    Occupant = ReadString(reader, "Occupant"),
                    StartDate = ReadDateTime(reader, "StartDate"),
                    EndDate = ReadDateTime(reader, "EndDate"),
                    ReservationType = ReadString(reader, "ReservationType"),
                    FacilityName = ReadString(reader, "FacilityName"),
                    RoomName = ReadString(reader, "RoomName"),
                    DateOfStay = ReadString(reader, "DateOfStay"),
                    NoOfDays = ReadInt32(reader, "NoOfDays")
                });
            }

            return reportData;
        }

        private static string ReadString(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
        }

        private static int ReadInt32(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static decimal ReadDecimal(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
        }

        private static DateTime ReadDateTime(SqlDataReader reader, string name)
        {
            var ordinal = reader.GetOrdinal(name);
            return reader.IsDBNull(ordinal) ? DateTime.MinValue : Convert.ToDateTime(reader.GetValue(ordinal));
        }
    }

}

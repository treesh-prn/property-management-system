using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Data;

namespace PropertyManagementSystem.Services
{
    public class ReportServices
    {
        private readonly DAL _context;
        private readonly string _connectionString;

        public ReportServices(DAL context, IConfiguration configuration)
        {
            _context = context;
            _connectionString = configuration.GetConnectionString("ApplicationDbContextConnection");
        }

        public async Task<List<ReservationReport>> GetCompanyChargesReportAsync(DateTime? startDate, DateTime? endDate, string preparedBy)
        {
            var reportData = new List<ReservationReport>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = new SqlCommand("dbo.GenerateCompanyChargesReport", connection))
                {
                    command.CommandType = CommandType.StoredProcedure;

                    // Add parameters
                    command.Parameters.AddWithValue("@StartDate", startDate.HasValue ? (object)startDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@EndDate", endDate.HasValue ? (object)endDate.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@PreparedBy", preparedBy);

                    // Execute the stored procedure and read the results
                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var data = new ReservationReport
                            {
                                CompanyCode = reader["CompanyCode"].ToString(),
                                Department = reader["Department"].ToString(),
                                RoomUsed = reader["RoomUsed"].ToString(),
                                Dates = reader["Dates"].ToString(),
                                NoOfDays = reader.GetInt32(reader.GetOrdinal("NoOfDays")),
                                EquipmentUsed = reader["EquipmentUsed"].ToString(),
                                RoomCharge = reader.GetDecimal(reader.GetOrdinal("RoomCharge")),
                                EquipmentCharge = reader.GetDecimal(reader.GetOrdinal("EquipmentCharge")),
                                TotalCharge = reader.GetDecimal(reader.GetOrdinal("TotalCharge")),
                                PreparedBy = reader["PreparedBy"].ToString(),
                                NotedBy = reader["NotedBy"].ToString()
                            };
                            reportData.Add(data);
                        }
                    }
                }
            }

            return reportData;
        }

    }

}

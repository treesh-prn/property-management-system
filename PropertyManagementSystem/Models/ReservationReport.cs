namespace PropertyManagementSystem.Models
{
    public class ReservationReport
    {
        public string CompanyCode { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string RoomUsed { get; set; } = string.Empty;
        public string Dates { get; set; } = string.Empty;
        public int NoOfDays { get; set; }
        public string EquipmentUsed { get; set; } = string.Empty;
        public decimal RoomCharge { get; set; }
        public decimal EquipmentCharge { get; set; }
        public decimal TotalCharge { get; set; }
        public string PreparedBy { get; set; } = string.Empty;
        public string NotedBy { get; set; } = string.Empty;
    }

    public sealed class OccupancyReportRow
    {
        public string ReservationCode { get; set; } = string.Empty;
        public string Requestor { get; set; } = string.Empty;
        public string Occupant { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string ReservationType { get; set; } = string.Empty;
        public string FacilityName { get; set; } = string.Empty;
        public string RoomName { get; set; } = string.Empty;
        public string DateOfStay { get; set; } = string.Empty;
        public int NoOfDays { get; set; }
    }
}

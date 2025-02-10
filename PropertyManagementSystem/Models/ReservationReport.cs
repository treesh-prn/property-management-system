namespace PropertyManagementSystem.Models
{
    public class ReservationReport
    {
        public string CompanyCode { get; set; }
        public string Department { get; set; }
        public string RoomUsed { get; set; }
        public string Dates { get; set; }
        public int NoOfDays { get; set; }
        public string EquipmentUsed { get; set; }
        public decimal RoomCharge { get; set; }
        public decimal EquipmentCharge { get; set; }
        public decimal TotalCharge { get; set; }
        public string PreparedBy { get; set; }
        public string NotedBy { get; set; }
    }
}

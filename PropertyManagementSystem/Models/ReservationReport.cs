namespace PropertyManagementSystem.Models
{
    public class ReservationReport
    {
        public string CompanyName { get; set; }
        public double EquipmentTotalCost { get; set; }
        public double RoomTotalCost { get; set; }
        public double SubTotalCost { get; set; }
        public double TotalCost { get; set; }
        public double? Hours { get; set; }
    }
}

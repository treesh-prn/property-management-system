namespace PropertyManagementSystem.Models
{
    public class CompanyMonthlyChargeReport
    {
        public int CompanyId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public double TotalRoomCharges { get; set; }
        public double TotalEquipmentCharges { get; set; }
        public double TotalCharges { get; set; }
    }
}

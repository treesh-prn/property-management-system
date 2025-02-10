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

        public List<DepartmentCharge> Departments { get; set; }
    }

    public class DepartmentCharge
    {
        public string DepartmentName { get; set; }
        public double FacilityCharge { get; set; }
        public double EquipmentCharge { get; set; }
        public double TotalCharges { get; set; }
    }
}

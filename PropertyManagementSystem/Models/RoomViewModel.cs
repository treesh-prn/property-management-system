namespace PropertyManagementSystem.Models
{
    public class RoomViewModel
    {
        public int RoomId { get; set; }
        public string RoomName { get; set; }
        public string RoomStatus { get; set; }
        public int FacilityId { get; set; }
        public string FacilityName { get; set; }
        public string FacilityLocation { get; set; }
        public int Capacity { get; set; }
        public bool isActive { get; set; }
        public int RoomType { get; set; }
        public double? CostPerDay { get; set; }
        public double? CostPerWeek { get; set; }
        public double? CostPerMonth { get; set; }
        public bool? isPerHead { get; set; }
    }
}

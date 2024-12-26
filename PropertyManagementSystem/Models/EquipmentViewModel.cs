namespace PropertyManagementSystem.Models
{
    public class EquipmentViewModel
    {
        public int Id { get; set; }
        public string? EquipmentName { get; set; }
        public int? EquipmentQty { get; set; }
        public double? PurchaseCost { get; set; }
        public double? WholeDayCost { get; set; }
        public double? HalfDayCost { get; set; }
        public string? Remarks { get; set; }
        public bool? isActive { get; set; }
        public int? FacilityId { get; set; }
        public int? RoomId { get; set; }
        public bool? isFacilityEquipment { get; set; }
        public string? FacilityName { get; set; }
        public string? RoomName { get; set; }
    }
}

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
        public string LinkType { get; set; } = EquipmentLinkTypes.Included;
        public int DefaultQuantity { get; set; } = 1;
        public bool IsFixed { get; set; }
        public List<EquipmentLocationLinkViewModel> LocationLinks { get; set; } = new();
    }

    public class EquipmentRequest
    {
        public string? _name { get; set; }
        public int _quantity { get; set; }
        public double _purCost { get; set; }
        public double _halfCost { get; set; }
        public double _wholeCost { get; set; }
        public string? _remarks { get; set; }
        public bool _isActive { get; set; }
        public int? _facilityId { get; set; }
        public int? _roomId { get; set; }
        public bool _isFacilityEquipment { get; set; }
        public string _linkType { get; set; } = EquipmentLinkTypes.Included;
        public int _defaultQuantity { get; set; } = 1;
        public bool _isFixed { get; set; }
        public bool _preserveLocationLinks { get; set; }
    }

    public class EditEquipmentRequest : EquipmentRequest
    {
        public int Id { get; set; }
    }

    public sealed class EquipmentLocationLinkViewModel
    {
        public int FacilityId { get; set; }
        public int? RoomId { get; set; }
        public string FacilityName { get; set; } = string.Empty;
        public string? RoomName { get; set; }
        public string LinkType { get; set; } = EquipmentLinkTypes.Included;
        public int DefaultQuantity { get; set; }
        public bool IsFixed { get; set; }
    }
}

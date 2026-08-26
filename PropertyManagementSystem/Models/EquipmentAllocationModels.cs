using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    public static class EquipmentLinkTypes
    {
        public const string Included = "INCLUDED";
        public const string AddOn = "ADD_ON";
    }

    public static class EquipmentAllocationSources
    {
        public const string Facility = "FACILITY";
        public const string Room = "ROOM";
        public const string Manual = "MANUAL";
    }

    public static class EquipmentAllocationStatuses
    {
        public const string Reserved = "RESERVED";
        public const string Released = "RELEASED";
    }

    [Table("GFMS_FacilityEquipmentLinks")]
    public sealed class GFMS_FacilityEquipmentLink
    {
        [Key]
        public int FacilityEquipmentLinkId { get; set; }
        public int FacilityId { get; set; }
        public int EquipmentId { get; set; }
        public int? RoomId { get; set; }
        public int DefaultQuantity { get; set; } = 1;
        [MaxLength(20)]
        public string LinkType { get; set; } = EquipmentLinkTypes.Included;
        public bool IsFixed { get; set; }
        public bool IsActive { get; set; } = true;
    }

    [Table("GFMS_ReservationEquipments")]
    public sealed class GFMS_ReservationEquipment
    {
        [Key]
        public int ReservationEquipmentId { get; set; }
        public int ReservationDetailId { get; set; }
        public int EquipmentId { get; set; }
        public int Quantity { get; set; } = 1;
        [MaxLength(20)]
        public string SourceType { get; set; } = EquipmentAllocationSources.Manual;
        public int? SourceFacilityId { get; set; }
        public int? SourceRoomId { get; set; }
        public bool IsAutomatic { get; set; }
        [MaxLength(20)]
        public string AllocationStatus { get; set; } = EquipmentAllocationStatuses.Reserved;
        [Column(TypeName = "decimal(18,2)")]
        public decimal AssignedCost { get; set; }
        [MaxLength(200)]
        public string EquipmentNameSnapshot { get; set; } = string.Empty;
        public DateTime DateAssigned { get; set; } = DateTime.UtcNow;
    }

    public sealed class FacilityEquipmentLinkInput
    {
        public int EquipmentId { get; set; }
        public bool Selected { get; set; }
        public int DefaultQuantity { get; set; } = 1;
        public string LinkType { get; set; } = EquipmentLinkTypes.Included;
        public bool IsFixed { get; set; }
    }

    public sealed class FacilityEquipmentLinkOption
    {
        public int EquipmentId { get; set; }
        public string EquipmentName { get; set; } = string.Empty;
        public int AvailableQuantity { get; set; }
        public bool Selected { get; set; }
        public int DefaultQuantity { get; set; } = 1;
        public string LinkType { get; set; } = EquipmentLinkTypes.Included;
        public bool IsFixed { get; set; }
    }

    public sealed class ReservationEquipmentViewModel
    {
        public int EquipmentId { get; set; }
        public string EquipmentName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string SourceType { get; set; } = string.Empty;
        public bool IsAutomatic { get; set; }
    }
}

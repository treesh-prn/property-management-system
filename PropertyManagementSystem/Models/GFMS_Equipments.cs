using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_Equipments")]
    public class GFMS_Equipments
    {
        [Key]
        public int Id { get; set; }
        public string? EquipmentName { get; set; }
        public int? EquipmentQty { get; set; }
        public double? PurchaseCost { get; set; }
        public double? WholeDayCost { get; set; }
        public double? HalfDayCost { get; set; }
        public string? Remarks { get; set; }
        public bool? isActive { get; set; }
    }

    public partial class equipmentList
    {
        public int Id { get; set; }
        public string EquipmentName { get; set; }

    }
}

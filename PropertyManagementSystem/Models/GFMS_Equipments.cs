using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_Equipments")]
    public class GFMS_Equipments
    {
        [Key]
        public int EquipmentId { get; set; }
        public string? EquipmentName { get; set; }
        public string? EquipmentStatus { get; set; }
    }

    public partial class equipmentList
    {
        public int EquipmentId { get; set; }
        public string EquipmentName { get; set; }

    }
}

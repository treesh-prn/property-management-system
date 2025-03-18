using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_RoomEquipment")]
    public class GFMS_RoomEquipment
    {
        [Key]
        public int Id { get; set; }
        public int RoomId { get; set; }
        public int EquipmentId {  get; set; }
    }
}

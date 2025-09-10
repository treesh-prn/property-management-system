using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_ReservationEquipment")]
    public class GFMS_ReservationEquipment
    {
        [Key]
        public int ReservationDetailId { get; set; }
        [Key]
        public int EquipmentId { get; set; }

        public GFMS_ReservationDetails ReservationDetail { get; set; }
        public GFMS_Equipments Equipment { get; set; }

    }
}

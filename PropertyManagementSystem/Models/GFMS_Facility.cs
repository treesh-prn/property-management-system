using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_Facility")]
    public class GFMS_Facility
    {
        [Key]
        public int FacilityId { get; set; }

        public string? FacilityName { get; set; }
        public string? FacilityLocation { get; set; }
        public bool? isActive { get; set; }
    }
}

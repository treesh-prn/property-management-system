using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_Rooms")]
    public partial class GFMS_Rooms
    {
        public virtual GFMS_Facility Facility { get; set; }
        [Key]
        public int RoomId { get; set; }
        [Required]
        public string RoomName { get; set; }
        public string? RoomStatus { get; set; }
        public int FacilityId { get; set; }
        [Required]
        public int Capacity { get; set; }
        public bool isActive { get; set; }
        public int RoomType { get; set; }

    }
    public partial class facilityList
    { 
        public int FacilityId { get; set; }
        public string FacilityName { get; set; }
        public string? FacilityLocation { get; internal set; }
    }
}

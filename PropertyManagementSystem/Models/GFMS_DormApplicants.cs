using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_DormApplicants")]
    public class GFMS_DormApplicants
    {
        [Key]
        public int ApplicantId { get; set; }
        public string UserId { get; set; }
        public string FullName { get; set; }
        public string? PermanentAddress { get; set; }
        public string? CurrentAddress { get; set; }
        public string? ContactNumber { get; set; }
        public int? SchedType { get; set; }
        public int? HomeOwnership {  get; set; }
        public int? OwnedHouseLocation { get; set; }
        public string? SpecificLocation { get; set; }
        public int? ModeOfTransportation { get; set; }
        public string? PlateNumber { get; set; }
        public string? TravelTime { get; set; }
        public string? Fare {  get; set; }
        public bool? isCritical { get; set; }
        public bool? isOnCall { get; set; }
        public string? DeptHeadRemarks { get; set; }
        public DateTime DateAdded { get; set; }
    }
}
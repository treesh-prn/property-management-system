using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_HousingDependents")]
    public class GFMS_HousingDependents
    {
        [Key]
        public int DependentId { get; set; }
        public int ApplicantId { get; set; }
        public string? DependentName { get; set; }
        public string? DependentAge { get; set;}
        public string? SchoolName { get; set; }
        public string? SchoolLocation { get; set; }
        public string? CompanyName { get; set; }
        public string? CompanyLocation { get; set; }
        public string? Status { get; set; }
    }
}

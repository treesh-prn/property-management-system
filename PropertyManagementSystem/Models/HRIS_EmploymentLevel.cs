using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("HRIS_EmploymentLevel")]
    public class HRIS_EmploymentLevel
    {
        [Key]
        public int Id { get; set; }
        public string? Name { get; set; }
    }
}

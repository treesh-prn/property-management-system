using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("Gen_Department")]
    public class Gen_Department
    {
        [Key]
        public int Id { get; set; }
        public string? DepartmentName { get; set; }
        public string? DepartmentCode { get; set; }
        public int CompanyId { get; set; }
        public Guid? DepartmentHead {  get; set; }
    }
}

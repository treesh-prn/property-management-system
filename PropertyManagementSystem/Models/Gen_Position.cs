using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("Gen_Position")]
    public class Gen_Position
    {
        [Key]
        public int Id { get; set; }
        [Required] 
        public string PositionCode { get; set; }
        [Required]
        public string PositionName { get; set; }
        public bool IsActive { get; set; }
    }
}

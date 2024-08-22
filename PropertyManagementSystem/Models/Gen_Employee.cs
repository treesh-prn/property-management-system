using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("Gen_Employee")]
    public class Gen_Employee
    {
        [Key]
        public int ID { get; set; }
        public Guid GUID { get; set; }
        public string? EmployeeID { get; set; }
        public string? Firstname { get; set; }
        public string? Middlename { get; set; }
        public string? Lastname { get; set; }
        public int? CompanyId { get; set; }
        public int? DivisionId {  get; set; }
        public int? DepartmentId { get; set; }
        public int? SectionId { get; set; }
        public int? PositionId { get; set; }
        public int EmploymentLevelId { get; set; }
        public int EmploymentStatusId { get; set; }
        public string? Email { get; set; }
        public string? Image { get; set; }
        public string? RFID { get; set; }
        public string? UserId { get; set; }
        public string? status { get; set; }
        public DateTime? Birthdate { get; set; }

        [NotMapped]
        public string Fullname => $"{Firstname} {Lastname}";
        [NotMapped]
        public string NameInitials => $"{Firstname[0]}{Lastname[0]}";
    }
}

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using static Microsoft.EntityFrameworkCore.DbLoggerCategory.Database;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_ReservationDetails")]
    public class GFMS_ReservationDetails
    {
        [Key]
        public int ReservationDetailId { get; set; }
        public int ReservationId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string GuestName { get; set; }
        public string Purpose { get; set; }
        public string? Company { get; set; }
        public string? Relationship { get; set; }
        public DateTime? DateAdded { get; set; }
        public int EmployeeID { get; set; }

        public int RoomId { get; set; }
        public int FacilityId { get; set; }

        public GFMS_ReservationHeader ReservationHeader { get; set; }
    }
}

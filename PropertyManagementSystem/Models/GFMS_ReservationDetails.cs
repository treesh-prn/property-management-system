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
        public string? GuestName { get; set; }
        public string? Purpose { get; set; }
        public string? Company { get; set; }
        public string? Relationship { get; set; }
        public DateTime? DateAdded { get; set; }
        public int EmployeeID { get; set; }

        public int RoomId { get; set; }
        public int FacilityId { get; set; }
        public int NumberOfGuest {  get; set; }
        public string? EquipmentNeeded {  get; set; }

        public int? AppliedUnit {  get; set; }
        public string? ContactNumber { get; set; }
        public string? PermanentAddress { get; set; }
        public string? CurrentAddress { get; set; }
        public int? HomeOwnership {  get; set; }
        public int? OwnedHouseLocation {  get; set; }
        public string? SpecificLocation { get; set; }
        public int? ModeOfTransportation { get; set; }
        public string? VehicleType { get; set; }
        public string? TravelTime { get; set; }
        public string? Fare {  get; set; }
        public string? CivilStatus { get; set; }
        public string? FileAttachment {  get; set; }
        public string? DependentName { get; set; }
        public string? DependentAge { get; set; }
        public string? StudyLocation { get; set; }
        public string? WorkPosition { get; set; }
        public int? ScheduleType { get; set; }
        public bool? isCritical { get; set; }
        public bool? isOnCall { get; set; }
        public string? DeptHeadRemarks { get; set; }
        public string? OtherInfo { get; set; }

        public GFMS_ReservationHeader ReservationHeader { get; set; }
        public virtual GFMS_Rooms Room { get; set; }
        public virtual GFMS_Facility Facility { get; set; }
    }
}

using Kendo.Mvc.UI;

namespace PropertyManagementSystem.Models
{
    public class ReservationViewModel : ISchedulerEvent
    {
        public GFMS_ReservationHeader? ReservationHeader { get; set; }
        public GFMS_Facility? Facility { get; set; }
        public GFMS_Rooms? Room { get; set; }
        public Gen_Department? Department { get; set; }
        public Gen_Company? Companies { get; set; }
        public Gen_Employee? Employees { get; set; }
        public Gen_Position? Position { get; set; }
        public List<string> Equipments { get; set; }
        public HRIS_EmploymentLevel? EmploymentLevel { get; set; }
        public List<GFMS_ReservationDetails>? ReservationDetails { get; set; }
        public List<DormerHistoryViewModel>? DormerHistoryDetails { get; set; }
        public List<GFMS_FileAttachment> FileAttachments { get; set; }


        public int FacilityId { get; set; }
        public string? FacilityName { get; set; }
        public string? FacilityLocation { get; set; }
        public int? ReservationDetailId { get; set; }
        public int ReservationId { get; set; }
        public string ReservationType { get; set; }
        public string ContactNumber { get; set; }
        public string DepartmentName { get; set; }
        public int RoomId { get; set; }
        public string? RoomName { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string? Title { get; set; }
        public bool IsAllDay { get; set; }
        public string? Description { get; set; }
        public string? RecurrenceException { get; set; }
        public string? RecurrenceID { get; set; }
        public string? RecurrenceRule { get; set; }
        public string? StartTimezone { get; set; }
        public string? EndTimezone { get; set; }

        // Additional properties for the header model
        public string? Requestor { get; set; }
        public string? RequestorID { get; set; }
        public string? RequestorEmail { get; set; }
        public string? GUID { get; set; }
        public string? GuestName { get; set; }
        public string? Status { get; set; }
        public string? Relationship { get; set; }
        public string? Company { get; set; }
        public bool? isPersonal { get; set; }
        public bool? isFreeOfCharge { get; set; }
        public bool? CanDelete { get; set; }
        public DateTime? DateSubmitted { get; set; }
        public string? ReservationCode { get; set; }
        public string? Approver { get; set; }
        public DateTime? ApprovedDateTime { get; set; }
        public string? ApproverEmail { get; set; }
        public int ChargeToCompany { get; set; }
        public int NumberOfGuest { get; set; }
        public int OccupantCount { get; set; }
        public string? Purpose { get; set; }
        public string? EquipmentNeeded {  get; set; }
        public List<string>? EquipmentNames {  get; set; }

        public int? ScheduleType {  get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public DateTime DateFiled { get; set; }

        //count for dashboard
        public int NewFNReservationsCount { get; set; }
        public int NewGHReservationsCount { get; set; }
        public int NewDRMReservationsCount { get; set; }
        public int ActiveGuestCount { get; set; }
        public int OccupiedRoomsCount { get; set; }
        public int AvailableRoomsCount { get; set; }
        public IEnumerable<ReservationViewModel>? Reservations { get; set; }

        //hours spent
        public double? HourSpent { get; set; }
        public double? EquipmentTotalCost {  get; set; }
        public double? RoomTotalCost { get; set; }
        public double SubTotalCost {  get; set; }
        public double TotalCost {  get; set; }

        //total booking for today
        public IEnumerable<dynamic> TodaysBookings { get; set; }
    }
}

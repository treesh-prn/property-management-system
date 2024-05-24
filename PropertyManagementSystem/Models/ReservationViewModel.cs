using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PropertyManagementSystem.Models
{
    public class ReservationViewModel : ISchedulerEvent
    {
        public GFMS_ReservationHeader ReservationHeader { get; set; }
        public GFMS_ReservationDetails ReservationDetails { get; set; }

        public int FacilityId { get; set; }
        public int ReservationDetailId { get; set; }
        public int ReservationId { get; set; }
        public int RoomId { get; set; }
        public string RoomName { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public string Title { get; set; }
        public bool IsAllDay { get; set; }
        public string Description { get; set; }
        public string RecurrenceException { get; set; }
        public string RecurrenceID { get; set; }
        public string RecurrenceRule { get; set; }
        public string StartTimezone { get; set; }
        public string EndTimezone { get; set; }

        // Additional properties for the header model
        public string Requestor { get; set; }
        public string RequestorEmail { get; set; }
        public string GUID { get; set; }
        public int Status { get; set; }
        public DateTime? DateSubmitted { get; set; }
        public string? ReservationCode { get; set; }
        public string? Approver { get; set; }
        public DateTime? ApprovedDateTime { get; set; }
        public string? ApproverEmail { get; set; }
        public int ChargeToCompany { get; set; }

        public string Purpose { get; set; }

        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

    }
}

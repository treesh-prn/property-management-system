namespace PropertyManagementSystem.Models
{
    public class ReservationDetailViewModel
    {
        public int ChargeToCompany { get; set; }
        public string Requestor { get; set; }
        public string RequestorEmail { get; set; }
        public string GUID { get; set; }
        public int Status { get; set; }
        public int FacilityId { get; set; }
        public int RoomId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Purpose { get; set; }
    }
}

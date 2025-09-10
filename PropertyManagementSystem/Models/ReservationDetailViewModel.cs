namespace PropertyManagementSystem.Models
{
    public class ReservationDetailViewModel
    {
        public GFMS_ReservationHeader ReservationHeader { get; set; }
        public Gen_Department Gen_Department { get; set; }
        public List<GFMS_ReservationDetails> ReservationDetails { get; set; }
        public GFMS_DormApplicants DormApplicants { get; set; }
        public int NumberOfGuest {  get; set; }
        public bool CanEdit { get; set; }

    }
}

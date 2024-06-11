namespace PropertyManagementSystem.Models
{
    public class ReservationDetailViewModel
    {
        public GFMS_ReservationHeader ReservationHeader { get; set; }
        public List<GFMS_ReservationDetails> ReservationDetails { get; set; }
        public int NumberOfGuest {  get; set; }

    }
}

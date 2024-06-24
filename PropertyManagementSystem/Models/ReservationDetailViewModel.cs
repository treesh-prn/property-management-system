namespace PropertyManagementSystem.Models
{
    public class ReservationDetailViewModel
    {
        public GFMS_ReservationHeader ReservationHeader { get; set; }
        public Gen_Department Gen_Department { get; set; }
        public List<GFMS_ReservationDetails> ReservationDetails { get; set; }
        public int NumberOfGuest {  get; set; }

    }
}

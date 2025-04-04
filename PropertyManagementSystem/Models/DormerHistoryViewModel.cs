namespace PropertyManagementSystem.Models
{
    public class DormerHistoryViewModel
    {
        public int DormerId { get; set; }
        public string PreviousRoom { get; set; }
        public string NewRoom { get; set; }
        public string PreviousFacility { get; set; }
        public string NewFacility { get; set; }
        public DateTime? TransferDate { get; set; }
        public string? TransferredBy { get; set; }
        public string? Status { get; set; }
    }
}

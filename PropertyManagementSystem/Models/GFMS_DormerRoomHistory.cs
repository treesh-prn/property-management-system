namespace PropertyManagementSystem.Models
{
    public class GFMS_DormerRoomHistory
    {
        public int Id { get; set; }
        public string? DormerId { get; set; }
        public int PreviousRoomId { get; set; }
        public int? NewRoomId { get; set; }
        public int PreviousFacilityId { get; set; }
        public int? NewFacilityId { get; set; }
        public DateTime? TransferDate { get; set; }
        public string? TransferredBy { get; set; }
        public string? Status { get; set; }
    }
}

namespace PropertyManagementSystem.Models
{
    public class RoomOccupantViewModel
    {
        public int ID { get; set; }
        public int? ParentID { get; set; }
        public string? Name { get; set; }
        public string? Title { get; set; }
        public bool Expanded { get; set; }
    }

}

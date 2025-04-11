using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_FileAttachment")]
    public class GFMS_FileAttachment
    {
        public int Id { get; set; }
        public int ReservationId { get; set; }
        public string FileName { get; set; }
        public string FilePath { get; set; }
        public DateTime DateUploaded { get; set; }

    }
}

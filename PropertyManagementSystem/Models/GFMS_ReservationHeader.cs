using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("GFMS_ReservationHeader")]
    public class GFMS_ReservationHeader
    {
        [Key]
        public int ReservationId { get; set; }

        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:dd/MM/yyyy}")]
        [Column(TypeName = "DateTime")]
        public DateTime? DateFiled { get; set; }
        public string? Requestor { get; set; }
        public string? RequestorEmail { get; set; }
        public string? GUID { get; set; }
        public string? Status { get; set; }
        [DisplayFormat(ApplyFormatInEditMode = true, DataFormatString = "{0:dd/MM/yyyy}")]
        [Column(TypeName = "DateTime")]
        public DateTime? DateSubmitted { get; set; }
        public string? ReservationCode { get; set; } 
        public string? Approver { get; set; } 
        public DateTime? ApprovedDateTime { get; set; } 
        public string? ApproverEmail { get; set; }
        public int ChargeToCompany { get; set; }
        public int? Department { get; set; }
        public string? ReservationType { get; set; }
        public string? Remarks { get; set; }
        public string? ApprovedBy {  get; set; }

        //public ICollection<GFMS_ReservationDetails> ReservationDetails { get; set; }
    }
}

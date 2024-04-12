using Microsoft.AspNetCore.Components.Forms;
using System.ComponentModel.DataAnnotations.Schema;

namespace PropertyManagementSystem.Models
{
    [Table("Gen_Company")]
    public class Gen_Company
    {
        public int Id { get; set; } 
        public string Company {  get; set; }
        public string CompanyCode { get; set;}
    }
}

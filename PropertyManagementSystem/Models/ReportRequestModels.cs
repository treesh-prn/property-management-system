using System.ComponentModel.DataAnnotations;

namespace PropertyManagementSystem.Models
{
    public abstract class ReportPeriodRequest : IValidatableObject
    {
        [Required(ErrorMessage = "Select a start date.")]
        [DataType(DataType.Date)]
        public DateTime? StartDate { get; set; }

        [Required(ErrorMessage = "Select an end date.")]
        [DataType(DataType.Date)]
        public DateTime? EndDate { get; set; }

        public bool Download { get; set; }

        protected virtual int MaximumRangeDays => 366;

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!StartDate.HasValue || !EndDate.HasValue)
            {
                yield break;
            }

            if (EndDate.Value.Date < StartDate.Value.Date)
            {
                yield return new ValidationResult(
                    "End date must be on or after the start date.",
                    new[] { nameof(EndDate) });
                yield break;
            }

            var rangeDays = (EndDate.Value.Date - StartDate.Value.Date).TotalDays + 1;
            if (rangeDays > MaximumRangeDays)
            {
                yield return new ValidationResult(
                    $"Select a date range of {MaximumRangeDays:N0} days or fewer.",
                    new[] { nameof(EndDate) });
            }
        }
    }

    public sealed class CompanyChargesReportRequest : ReportPeriodRequest
    {
        [Required(ErrorMessage = "Enter the name of the report preparer.")]
        [StringLength(100, ErrorMessage = "Prepared by must not exceed 100 characters.")]
        public string PreparedBy { get; set; } = string.Empty;
    }

    public sealed class OccupancyReportRequest : ReportPeriodRequest
    {
        [Required(ErrorMessage = "Select a reservation type.")]
        public string ReservationType { get; set; } = string.Empty;

        protected override int MaximumRangeDays => 31;
    }
}

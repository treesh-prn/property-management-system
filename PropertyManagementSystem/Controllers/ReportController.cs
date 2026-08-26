using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagementSystem.Attributes;
using PropertyManagementSystem.Models;
using PropertyManagementSystem.Services;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    [ActiveRoleAuthorize("GFMS.System Admin", "GFMS.Admin Approver", "GFMS.Admin Manager")]
    public class ReportController : Controller
    {
        private static readonly HashSet<string> ReservationTypes =
            new(StringComparer.OrdinalIgnoreCase) { "FN", "GH", "DRM" };

        private readonly ReportServices _reportServices;
        private readonly CompanyChargesPdfService _companyChargesPdfService;
        private readonly OccupancyPdfService _occupancyPdfService;
        private readonly ExcelReportService _excelReportService;
        private readonly IRoleService _roleService;
        private readonly ILogger<ReportController> _logger;

        public ReportController(
            ReportServices reportServices,
            CompanyChargesPdfService companyChargesPdfService,
            OccupancyPdfService occupancyPdfService,
            ExcelReportService excelReportService,
            IRoleService roleService,
            ILogger<ReportController> logger)
        {
            _reportServices = reportServices ?? throw new ArgumentNullException(nameof(reportServices));
            _companyChargesPdfService = companyChargesPdfService ?? throw new ArgumentNullException(nameof(companyChargesPdfService));
            _occupancyPdfService = occupancyPdfService ?? throw new ArgumentNullException(nameof(occupancyPdfService));
            _excelReportService = excelReportService ?? throw new ArgumentNullException(nameof(excelReportService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IActionResult Index()
        {
            return View(new OccupancyReportRequest
            {
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(6),
                ReservationType = "FN"
            });
        }

        public async Task<IActionResult> MonthlyChargesReport()
        {
            var employee = await _roleService.GetUserName(User);
            var today = DateTime.Today;

            return View(new CompanyChargesReportRequest
            {
                StartDate = new DateTime(today.Year, today.Month, 1),
                EndDate = today,
                PreparedBy = employee?.Fullname ?? User.Identity?.Name ?? string.Empty
            });
        }

        [HttpGet]
        public async Task<IActionResult> GenerateReport(
            [FromQuery(Name = "_startDate")] DateTime? startDate,
            [FromQuery(Name = "_endDate")] DateTime? endDate,
            [FromQuery(Name = "_preparedBy")] string? preparedBy,
            [FromQuery(Name = "download")] bool download,
            [FromQuery(Name = "format")] string? format,
            CancellationToken cancellationToken)
        {
            var request = new CompanyChargesReportRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                PreparedBy = preparedBy?.Trim() ?? string.Empty,
                Download = download
            };

            ModelState.Clear();
            TryValidateModel(request);
            if (!ModelState.IsValid)
            {
                return View("MonthlyChargesReport", request);
            }

            try
            {
                var rows = await _reportServices.GetCompanyChargesReportAsync(
                    request.StartDate!.Value,
                    request.EndDate!.Value,
                    request.PreparedBy,
                    cancellationToken);
                if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
                {
                    var workbook = _excelReportService.GenerateCharges(
                        rows, request.StartDate.Value, request.EndDate.Value, request.PreparedBy);
                    return File(
                        workbook,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"Company-Charges-{request.StartDate:yyyy-MM-dd}-to-{request.EndDate:yyyy-MM-dd}.xlsx");
                }
                var reportBytes = _companyChargesPdfService.Generate(
                    rows,
                    request.StartDate.Value,
                    request.EndDate.Value,
                    request.PreparedBy);
                var fileName = $"Company-Charges-{request.StartDate:yyyy-MM-dd}-to-{request.EndDate:yyyy-MM-dd}.pdf";

                return request.Download
                    ? File(reportBytes, "application/pdf", fileName)
                    : File(reportBytes, "application/pdf");
            }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                _logger.LogError(ex, "Company charges report generation failed for {StartDate} through {EndDate}.", request.StartDate, request.EndDate);
                Response.StatusCode = StatusCodes.Status500InternalServerError;
                ViewData["ReportError"] = "The billing data could not be retrieved from the report database. Verify the date range and stored procedure, then try again.";
                return View("MonthlyChargesReport", request);
            }
        }

        [HttpGet]
        public Task<IActionResult> GenerateWeelyOccupancy(
            [FromQuery(Name = "_startDate")] DateTime? startDate,
            [FromQuery(Name = "_endDate")] DateTime? endDate,
            [FromQuery(Name = "_facility")] string? reservationType,
            [FromQuery(Name = "download")] bool download,
            [FromQuery(Name = "format")] string? format,
            CancellationToken cancellationToken) =>
            GenerateWeeklyOccupancy(startDate, endDate, reservationType, download, format, cancellationToken);

        [HttpGet]
        public async Task<IActionResult> GenerateWeeklyOccupancy(
            [FromQuery(Name = "_startDate")] DateTime? startDate,
            [FromQuery(Name = "_endDate")] DateTime? endDate,
            [FromQuery(Name = "_facility")] string? reservationType,
            [FromQuery(Name = "download")] bool download,
            [FromQuery(Name = "format")] string? format,
            CancellationToken cancellationToken)
        {
            var request = new OccupancyReportRequest
            {
                StartDate = startDate,
                EndDate = endDate,
                ReservationType = reservationType?.Trim() ?? string.Empty,
                Download = download
            };

            ModelState.Clear();
            TryValidateModel(request);
            if (!string.IsNullOrWhiteSpace(request.ReservationType) &&
                !ReservationTypes.Contains(request.ReservationType))
            {
                ModelState.AddModelError(nameof(request.ReservationType), "Select a supported reservation type.");
            }

            if (!ModelState.IsValid)
            {
                return View("Index", request);
            }

            try
            {
                var rows = await _reportServices.GetOccupancyReportAsync(
                    request.StartDate!.Value,
                    request.EndDate!.Value,
                    request.ReservationType,
                    cancellationToken);
                if (string.Equals(format, "excel", StringComparison.OrdinalIgnoreCase))
                {
                    var workbook = _excelReportService.GenerateOccupancy(
                        rows, request.StartDate.Value, request.EndDate.Value, request.ReservationType);
                    return File(
                        workbook,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                        $"Occupancy-{request.ReservationType}-{request.StartDate:yyyy-MM-dd}-to-{request.EndDate:yyyy-MM-dd}.xlsx");
                }
                var reportBytes = _occupancyPdfService.Generate(
                    rows,
                    request.StartDate.Value,
                    request.EndDate.Value,
                    request.ReservationType);
                var fileName = $"Occupancy-{request.ReservationType}-{request.StartDate:yyyy-MM-dd}-to-{request.EndDate:yyyy-MM-dd}.pdf";

                return request.Download
                    ? File(reportBytes, "application/pdf", fileName)
                    : File(reportBytes, "application/pdf");
            }
            catch (Exception ex) when (ex is Microsoft.Data.SqlClient.SqlException or InvalidOperationException)
            {
                _logger.LogError(ex, "Occupancy report generation failed for {ReservationType}, {StartDate} through {EndDate}.", request.ReservationType, request.StartDate, request.EndDate);
                Response.StatusCode = StatusCodes.Status500InternalServerError;
                ViewData["ReportError"] = "The occupancy data could not be retrieved from the report database. Verify the date range and stored procedure, then try again.";
                return View("Index", request);
            }
        }
    }
}

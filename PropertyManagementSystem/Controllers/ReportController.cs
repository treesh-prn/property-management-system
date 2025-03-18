using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PropertyManagementSystem.Attributes;
using PropertyManagementSystem.Services;

namespace PropertyManagementSystem.Controllers
{
    [Authorize]
    public class ReportController : Controller
    {
        private readonly SsrsService _ssrsService;

        public ReportController(SsrsService ssrsService)
        {
            _ssrsService = ssrsService ?? throw new ArgumentNullException(nameof(ssrsService));
        }
        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
        public IActionResult Index()
        {
            return View();
        }
        [ActiveRoleAuthorize("GFMS.Admin Manager", "GFMS.Admin Approver")]
        // Monthly charges report
        public IActionResult MonthlyChargesReport()
        {
            return View();
        }

        // Action to trigger report generation

        public async Task<ActionResult> GenerateReport(string _startDate, string _endDate, string _company, string _preparedBy, string _classification, string reportType)
        {
            var parameters = new Dictionary<string, string>
            {
                { "StartDate", _startDate },
                { "EndDate", _endDate },
                { "CompanyId", _company }, // Ensure _company contains a valid ID
                { "FacilityClassification", _classification }, // New parameter
                { "PreparedBy", _preparedBy }
            };

            try
            {
                if (reportType == "excel")
                {
                    var reportBytes = await _ssrsService.GetReportAsExcelAsync("/PMS/GFMS_GenerateCompanyBilling", parameters);
                    return File(reportBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "CompanyBillingReport.xlsx");
                }
                else if (reportType == "pdf")
                {
                    var reportBytes = await _ssrsService.GetReportAsPdfAsync("/PMS/GFMS_GenerateCompanyBilling", parameters);
                    return File(reportBytes, "application/pdf", "CompanyBillingReport.pdf");
                }
                else
                {
                    return BadRequest("Invalid report type requested.");
                }
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating report: {ex.Message}");
            }
        }



        public async Task<ActionResult> GenerateWeelyOccupancy(string _startDate, string _endDate, string _preparedBy, string _facility)
        {
            var parameters = new Dictionary<string, string>
            {
                { "StartDate", _startDate },
                { "EndDate", _endDate },
                { "ReservationType", _facility }
            };

            try
            {
                var reportBytes = await _ssrsService.GetReportAsPdfAsync("/PMS/GFMS_GetReservationOccupancy", parameters);

                // Validate the PDF file format
                if (reportBytes.Length > 4 && reportBytes[0] == '%' && reportBytes[1] == 'P' && reportBytes[2] == 'D' && reportBytes[3] == 'F')
                {
                    return File(reportBytes, "application/pdf", "Report.pdf");
                }

                throw new Exception("The response is not a valid PDF file.");
            }
            catch (Exception ex)
            {
                return BadRequest($"Error generating report: {ex.Message}");
            }
        }

    }
}

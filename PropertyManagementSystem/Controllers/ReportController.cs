using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        public IActionResult Index()
        {
            return View();
        }

        // Monthly charges report
        public IActionResult MonthlyChargesReport()
        {
            return View();
        }

        // Action to trigger report generation
        public async Task<ActionResult> GenerateReport(string _startDate, string _endDate, string _preparedBy)
        {
            var parameters = new Dictionary<string, string>
            {
                { "StartDate", _startDate },
                { "EndDate", _endDate },
                { "PreparedBy", _preparedBy }
            };

            try
            {
                var reportBytes = await _ssrsService.GetReportAsPdfAsync("/PMS/GFMS_GenerateCompanyBilling", parameters);

                // Validate the PDF file format
                if (reportBytes.Length > 4 && reportBytes[0] == '%' && reportBytes[1] == 'P' && reportBytes[2] == 'D' && reportBytes[3] == 'F')
                {
                    return File(reportBytes, "application/pdf", "CompanyBillingReport.pdf");
                }

                throw new Exception("The response is not a valid PDF file.");
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

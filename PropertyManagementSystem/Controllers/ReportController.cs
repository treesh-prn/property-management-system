using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using PropertyManagementSystem.Services;
using System;
using System.Threading.Tasks;

namespace PropertyManagementSystem.Controllers
{
    public class ReportController : Controller
    {
        private readonly SsrsService _ssrsService;

        public ReportController(SsrsService ssrsService)
        {
            _ssrsService = ssrsService;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> ViewReport(string reportName, int reservationId)
        {
            var reportUrl = await _ssrsService.GetReportUrlAsync(reportName, reservationId);
            ViewBag.ReportUrl = reportUrl;
            return View();
        }

    }
}

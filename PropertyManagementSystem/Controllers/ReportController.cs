using Microsoft.AspNetCore.Mvc;

namespace PropertyManagementSystem.Controllers
{
    public class ReportController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}

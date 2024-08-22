using Microsoft.AspNetCore.Mvc;

namespace PropertyManagementSystem.Controllers
{
    public class ErrorController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }

        [Route("Error/{statusCode}")]
        public IActionResult HttpStatusCodeHandler(int statusCode)
        {
            ViewBag.IsErrorPage = true; // Indicate this is an error page

            switch (statusCode)
            {
                case 404:
                    ViewBag.ErrorMessage = "Sorry, the resource you requested could not be found.";
                    break;
                case 500:
                    ViewBag.ErrorMessage = "Sorry, there was an internal server error.";
                    break;
                default:
                    ViewBag.ErrorMessage = "An unexpected error occurred.";
                    break;
            }

            return View("Error"); // Return the error view
        }

        public IActionResult Error()
        {
            ViewBag.IsErrorPage = true; // Indicate this is an error page
            return View();
        }
    }
}

using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Net.NetworkInformation;
using System;

namespace PropertyManagementSystem.Controllers
{
    public class AccommodationController : Controller
    {
        private readonly DAL _context;

        public AccommodationController(DAL context)
        {
            _context = context;
        }
        public IActionResult Index()
        {
            return View();
        }
        public IActionResult FunctionRoom() { return View(); }
        public IActionResult newReservation(int _company)
        {
            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";

            GFMS_ReservationHeader _reservation = new GFMS_ReservationHeader() {
                GUID = unique,
                ChargeToCompany = _company,
                Requestor = "Jerwin Alimuin",
                RequestorEmail = "jerwin_alimuin@tecoindustrialpark.com",
                Approver = "Ken Dela Rosa",
                DateFiled = DateTime.Now,
                Status = 0

            };

            _context.ReservationHeaders.Add(_reservation);
            _context.SaveChanges();

            return RedirectToAction("ReservationDetails", new { Id = unique });

        }
        public IActionResult ReservationDetails(string Id)
        {
            var _reservation = _context.ReservationHeaders.AsQueryable().Where(x => x.GUID == Id).ToList();
            int _reservationId = _reservation.FirstOrDefault().ReservationId;

            List<GFMS_ReservationDetails> _ReservationDetails = _context.ReservationDetails.AsQueryable().Where(x => x.ReservationId == _reservationId).ToList();
            TempData["ResevationDetails"] = _ReservationDetails;

            return View(_reservation);

        }
        public ActionResult getGuestHouseReservation([DataSourceRequest] DataSourceRequest request)
        {
            List<GFMS_ReservationHeader> _reservations = _context.ReservationHeaders.AsQueryable().Where(x => x.Status == 0).ToList();
            DataSourceResult result = _reservations.ToDataSourceResult(request, _reservation => new
            {
                ReservationId = _reservation.ReservationId,
                DateFiled = _reservation.DateFiled,
                Requestor = _reservation.Requestor,
                RequestorEmail  =   _reservation.RequestorEmail,
                GUID = _reservation.GUID,
                Status = _reservation.Status,
                DateSubmitted = _reservation.DateSubmitted,
                ReservationCode = _reservation.ReservationCode,
                Approver = _reservation.Approver,
                ApprovedDateTime = _reservation.ApprovedDateTime,
                ApproverEmail = _reservation.ApproverEmail,
                ChargeToCompany = _reservation.ChargeToCompany
            });
            return Json(result);

        }
        public IActionResult ReservationList()
        { 


            return View();

            
        }

    }
}

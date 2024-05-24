using Kendo.Mvc.Extensions;
using Kendo.Mvc.UI;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;
using PropertyManagementSystem.Data;
using PropertyManagementSystem.Models;
using System.Net.NetworkInformation;
using System;
using Microsoft.EntityFrameworkCore;

namespace PropertyManagementSystem.Controllers
{
    public class AccommodationController : Controller
    {
        private readonly DAL _context;

        public AccommodationController(DAL context)
        {
            _context = context;
        }
        public IActionResult FunctionRoom()
        {

            var rooms = _context.Rooms
           .Include(r => r.Facility)
           .Where(x => x.RoomType == 1)
           .Select(room => new GFMS_Rooms
           {
               RoomId = room.RoomId,
               RoomName = room.RoomName,
               RoomType = room.RoomType,
               FacilityId = room.FacilityId
           })
           .ToList();

            var facilities = _context.Facilities
           .Where(facility => facility.Rooms.Any(room => room.RoomType == 1))
           .Select(facility => new GFMS_Facility
           {
               FacilityId = facility.FacilityId,
               FacilityName = facility.FacilityName,
           })
           .ToList();

            ViewData["Rooms"] = rooms;
            ViewData["Facilities"] = facilities;
            return View();
        }

        public IActionResult ViewReservation([DataSourceRequest] DataSourceRequest request)
        {
            var reservations = (from resDetails in _context.ReservationDetails
                                join resHeader in _context.ReservationHeaders on resDetails.ReservationId equals resHeader.ReservationId
                                select new ReservationViewModel
                                {
                                    ReservationId = resDetails.ReservationId,
                                    Requestor = resHeader.Requestor,
                                    ChargeToCompany = resHeader.ChargeToCompany,
                                    Start = resDetails.StartDate,
                                    End = resDetails.EndDate,
                                    Title = resDetails.Purpose,
                                    RoomId = resDetails.RoomId,
                                    FacilityId = resDetails.FacilityId
                                }).ToList();

            return Json(reservations.ToDataSourceResult(request));
        }

        [HttpPost]
        public IActionResult CreateReservation(int _company, string _requestor)
        {
            var guid1 = Guid.NewGuid().ToString("N").ToUpper();
            var unique = $"{guid1}";

            GFMS_ReservationHeader _reservation = new GFMS_ReservationHeader()
            {
                GUID = unique,
                ChargeToCompany = _company,
                Requestor = _requestor,
                RequestorEmail = User.Identity.Name,
                Approver = "Jerwin Alimuin",
                DateFiled = DateTime.Now,
                DateSubmitted = DateTime.Now,
                Status = 0

            };

            _context.ReservationHeaders.Add(_reservation);
            _context.SaveChanges();

            return RedirectToAction("ReservationDetails", new { Id = unique });

        }
        public IActionResult ReservationDetails(string Id)
        {
            var _reservation = _context.ReservationHeaders.FirstOrDefault(x => x.GUID == Id);

            if (_reservation == null)
            {
                return NotFound();
            }

            int _reservationId = _reservation.ReservationId;

            List<GFMS_ReservationDetails> _ReservationDetails = _context.ReservationDetails
                                                                        .Where(x => x.ReservationId == _reservationId)
                                                                        .ToList();
            TempData["ReservationDetails"] = _ReservationDetails;

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
                RequestorEmail = _reservation.RequestorEmail,
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

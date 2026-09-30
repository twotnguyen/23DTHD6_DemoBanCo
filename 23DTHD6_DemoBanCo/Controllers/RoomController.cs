using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.Controllers
{
    [Authorize]
    public class RoomController : Controller
    {
        private readonly AppDbContext _db;
        private readonly RoomService _roomService;

        public RoomController(AppDbContext db, RoomService roomService)
        {
            _db = db;
            _roomService = roomService;
        }

        /// <summary>Lấy Id người đang đăng nhập từ cookie.</summary>
        private int CurrentUserId
        {
            get
            {
                string? value = User.FindFirstValue(ClaimTypes.NameIdentifier);
                return int.TryParse(value, out int id) ? id : 0;
            }
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            int userId = CurrentUserId;

            var allRooms = await _roomService.GetRoomsWithDetailsAsync();
            var myRooms = await _roomService.GetRoomsByOwnerAsync(userId);

            ViewBag.MyRooms = myRooms;
            ViewBag.AllRooms = allRooms;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string? name)
        {
            int userId = CurrentUserId;

            if (userId == 0)
                return Challenge();

            var room = await _roomService.CreateRoomAsync(userId, name);
            return RedirectToAction("Details", new { id = room.Id });
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            int userId = CurrentUserId;

            var room = await _roomService.GetByIdAsync(id);
            if (room == null)
                return NotFound("Không tìm thấy phòng này.");

            ViewBag.IsOwner = room.OwnerId == userId;
            ViewBag.CurrentUserId = userId;
            return View(room);
        }

        /// <summary>Mở phòng theo mã, dùng khi người khác nhập mã phòng.</summary>
        [HttpGet]
        public async Task<IActionResult> Join(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return RedirectToAction("Index");

            var room = await _roomService.GetByCodeAsync(code);
            if (room == null)
            {
                TempData["ErrorMessage"] = "Không tìm thấy phòng với mã này.";
                return RedirectToAction("Index");
            }

            return RedirectToAction("Details", new { id = room.Id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            int userId = CurrentUserId;

            bool deleted = await _roomService.DeleteRoomAsync(id, userId);
            if (!deleted)
            {
                TempData["ErrorMessage"] = "Chỉ chủ phòng mới có thể xoá phòng này.";
                return RedirectToAction("Index");
            }

            return RedirectToAction("Index");
        }
    }
}
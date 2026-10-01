using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using _23DTHD6_DemoBanCo.Services;

namespace _23DTHD6_DemoBanCo.ViewComponents
{
    /// <summary>
    /// Chấm đỏ tin nhắn chưa đọc trên thanh điều hướng (đặc tả 5.2).
    ///
    /// VÌ SAO DÙNG VIEW COMPONENT MÀ KHÔNG TÍNH THẲNG TRONG LAYOUT:
    ///   - Số tin chưa đọc phải có ở MỌI trang của người đã đăng nhập, không chỉ trang
    ///     tin nhắn. Gọi service thủ công trong từng controller sẽ dễ sót chỗ và lặp lại
    ///     cùng một đoạn code.
    ///   - Nhưng gọi service trực tiếp trong _Layout thì lại không được: DI không được
    ///     tiêm vào view, và như vậy layout sẽ phụ thuộc thẳng vào CSDL.
    ///   View component là điểm giao đúng cho việc này: framework tự tiêm service,
    ///   chỉ chạy khi layout thật sự gọi tới nó, và không có đường ghi trùng.
    ///
    /// Lỗi ở đây chỉ làm mất chấm đỏ nên nuốt: một badge hỏng không được kéo theo cả
    /// thanh điều hướng của mọi trang.
    /// </summary>
    public class UnreadBadgeViewComponent : ViewComponent
    {
        private readonly DirectMessageService _directMessages;

        public UnreadBadgeViewComponent(DirectMessageService directMessages)
        {
            _directMessages = directMessages;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            string? value = HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (HttpContext.User.Identity?.IsAuthenticated != true
                || !int.TryParse(value, out int userId))
            {
                return Content(string.Empty);
            }

            try
            {
                int unread = await _directMessages.GetUnreadCountAsync(userId);
                return View(unread);
            }
            catch
            {
                // Badge là thứ phụ, hỏng nó không được làm sập cả trang.
                return Content(string.Empty);
            }
        }
    }
}
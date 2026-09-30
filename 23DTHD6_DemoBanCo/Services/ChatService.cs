using Microsoft.EntityFrameworkCore;
using _23DTHD6_DemoBanCo.Data;
using _23DTHD6_DemoBanCo.Models;

namespace _23DTHD6_DemoBanCo.Services
{
    /// <summary>
    /// Lưu và kiểm tra tin nhắn chat trong phòng.
    ///
    /// Kênh:
    ///   0 = PLAYERS_PRIVATE — chỉ 2 đấu thủ trong phòng được đọc/ghi.
    ///   1 = ROOM_PUBLIC     — cả đấu thủ và khán giả.
    ///
    /// Khán giả (RoomParticipant.Role == Spectator) không được gửi vào kênh riêng.
    /// Nội dung đi qua BadWordFilter trước khi ghi, nên dữ liệu đã lưu là dữ liệu sạch.
    /// </summary>
    public sealed class ChatService
    {
        public const int MaxMessageLength = 500;

        /// <summary>Số sticker tối đa gửi trong một tin nhắn.</summary>
        public const int MaxStickers = 12;

        public const int PlayersPrivateChannel = 0;
        public const int RoomPublicChannel = 1;

        /// <summary>
        /// Danh sách sticker hợp lệ. Dùng :king: thay cho shortcode đầu hàng vì ký tự
        /// quân cờ trong shortcode dễ bị lọc cầm chữ hoặc vỡ khi truyền qua SignalR.
        /// </summary>
        public static readonly IReadOnlyList<(string Code, string Emoji, string Label)> Stickers =
            new List<(string, string, string)>
            {
                (":clap:", "\U0001F44F", "Vỗ tay"),
                (":heart:", "❤️", "Tim"),
                (":thinking:", "\U0001F914", "Suy nghĩ"),
                (":sweat:", "\U0001F605", "Ngại"),
                (":cry:", "\U0001F62D", "Khóc"),
                (":tea:", "\U0001F375", "Trà"),
                (":fast:", "⚡", "Nhanh"),
                (":speechless:", "\U0001F910", "Không nói được"),
                (":ok:", "\U0001F44D", "OK"),
                (":wave:", "\U0001F91D", "Bắt tay"),
                (":flag:", "\U0001F3F3️", "Cờ"),
                (":fire:", "\U0001F525", "Lửa"),
                (":king:", "\U0001F451", "Vua")
            };

        private readonly AppDbContext _db;

        public ChatService(AppDbContext db)
        {
            _db = db;
        }

        /// <summary>
        /// Gửi một tin nhắn vào phòng.
        ///
        /// sticker khác null: phải nằm trong danh sách Stickers, Body để rỗng.
        /// sticker null: body là chữ, trim trước rồi kiểm tra độ dài 1..MaxMessageLength.
        ///
        /// Ném <see cref="InvalidOperationException"/> với thông báo tiếng Việt khi
        /// dữ liệu không hợp lệ — hub bắt và trả về cho người gửi, không phát cho cả phòng.
        /// </summary>
        public async Task<ChatMessage> SendRoomMessageAsync(
            int roomId,
            int userId,
            int channel,
            string body,
            string? sticker)
        {
            if (channel != PlayersPrivateChannel && channel != RoomPublicChannel)
            {
                throw new InvalidOperationException("Kênh chat không hợp lệ.");
            }

            if (!await CanAccessChannelAsync(roomId, userId, channel))
            {
                throw new InvalidOperationException("Bạn không có quyền gửi vào kênh này.");
            }

            int kind;
            string text;
            string? stickerCode = null;

            if (!string.IsNullOrWhiteSpace(sticker))
            {
                string requested = sticker.Trim();

                if (!IsKnownSticker(requested))
                {
                    throw new InvalidOperationException("Sticker không có trong danh sách cho phép.");
                }

                kind = 1;
                text = "";
                stickerCode = requested;
            }
            else
            {
                string trimmed = (body ?? "").Trim();

                if (trimmed.Length == 0)
                {
                    throw new InvalidOperationException("Nội dung tin nhắn không được để trống.");
                }

                if (trimmed.Length > MaxMessageLength)
                {
                    throw new InvalidOperationException(
                        $"Nội dung tin nhắn tối đa {MaxMessageLength} ký tự.");
                }

                kind = 0;
                text = BadWordFilter.Filter(trimmed);
            }

            var message = new ChatMessage
            {
                RoomId = roomId,
                UserId = userId,
                Channel = channel,
                Body = text,
                Kind = kind,
                Sticker = stickerCode,
                CreatedAt = DateTime.UtcNow
            };

            _db.ChatMessages.Add(message);
            await _db.SaveChangesAsync();

            return message;
        }

        /// <summary>
        /// Người này có được gửi/đọc vào kênh không.
        /// PLAYERS_PRIVATE chỉ dành cho hai đấu thủ, khán giả không vào được.
        /// </summary>
        public async Task<bool> CanAccessChannelAsync(int roomId, int userId, int channel)
        {
            var participant = await _db.RoomParticipants
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.RoomId == roomId && p.UserId == userId);

            if (participant == null)
            {
                return false;
            }

            if (channel == PlayersPrivateChannel)
            {
                return participant.Role == ParticipantRole.Player;
            }

            return true;
        }

        /// <summary>Lấy tin nhắn gần nhất của một kênh, mới nhất nằm cuối danh sách.</summary>
        public async Task<List<ChatMessage>> GetHistoryAsync(int roomId, int channel, int take = 100)
        {
            if (take <= 0)
            {
                return new List<ChatMessage>();
            }

            return await _db.ChatMessages
                .AsNoTracking()
                .Where(m => m.RoomId == roomId && m.Channel == channel)
                .OrderByDescending(m => m.Id)
                .Take(take)
                .OrderBy(m => m.Id)
                .ToListAsync();
        }

        /// <summary>Sticker có nằm trong danh sách cho phép không.</summary>
        public static bool IsKnownSticker(string code)
        {
            foreach (var sticker in Stickers)
            {
                if (string.Equals(sticker.Code, code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}

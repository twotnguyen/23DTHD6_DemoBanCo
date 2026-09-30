# 23DTHD6_DemoBanCo

Demo bàn cờ tướng (Chinese Chess) đa người chơi theo thời gian thực, xây dựng cho đồ án môn **QL-DA-CNTT**.

Mỗi người chơi mở một tab trình duyệt, di chuyển quân và chat trong cùng một phòng. Trạng thái ván đấu được đồng bộ qua **SignalR**; tính năng gọi video được dự kiến dùng **WebRTC** với SignalR làm kênh truyền tín hiệu (signaling).

---

## 1. Tổng quan

| Hạng mục | Giá trị |
|---|---|
| Framework | ASP.NET Core MVC 8.0 (`net8.0`) |
| Giao tiếp thời gian thực | SignalR (WebSocket) |
| Frontend | Vue 3 (Options API) tải từ CDN — không có bước build |
| WebRTC | Gọi video 1–1, STUN công cộng `stun.l.google.com:19302` |
| Cơ sở dữ liệu | Không có — toàn bộ trạng thái nằm ở client |
| Package NuGet | 0 (chỉ dùng framework có sẵn) |
| Test | Chưa có |

### Công nghệ và lý do chọn

- **ASP.NET Core 8 MVC** — phần render trang và phục vụ `wwwroot`.
- **SignalR** — kênh truyền tin nhắn/nước đi hai chiều giữa các client, hỗ trợ group và tự kết nối lại.
- **Vue 3 qua CDN** — toàn bộ logic client nằm trong một file `cotuong.js`, không cần Node.js hay bundler. Đổi lại: không có type-checking, không minify, không hot-reload.
- **WebRTC** — truyền trực tiếp A/V giữa hai trình duyệt để không phụ thuộc server media. Chưa có TURN server, nên chỉ hoạt động khi mạng của hai bên cho phép kết nối trực tiếp.

### Vai trò từng thành phần

| Thành phần | Trách nhiệm |
|---|---|
| `Program.cs` | Khởi tạo app, đăng ký MVC + SignalR, ánh xạ route `/chessHub` |
| `Hubs/ChessHub.cs` | Tiếp nhận lệnh từ client và phát lại cho các client khác |
| `Controllers/HomeController.cs` | Render trang `Index` (bàn cờ) và `Privacy` |
| `Controllers/api/BanCoController.cs` | Trả về vị trí khởi tạo của 32 quân từ file JSON |
| `Models/ChessMove.cs` | Dữ liệu một nước đi được gửi qua SignalR |
| `Views/Home/Index.cshtml` | Khung HTML của bàn cờ, panel video, khung chat |
| `wwwroot/js/cotuong.js` | Toàn bộ logic client: render quân, điều khiển, chat, WebRTC |
| `wwwroot/co_tuong_initial.json` | Vị trí khởi tạo của 32 quân cờ |
| `wwwroot/images/` | Ảnh nền bàn cờ và 32 hình quân dạng SVG |

---

## 2. Cách chạy

### Yêu cầu

| Yêu cầu | Ghi chú |
|---|---|
| [.NET SDK 8.0](https://dotnet.microsoft.com/download) | Bắt buộc; kiểm tra bằng `dotnet --version` |
| Trình duyệt | Chrome, Edge hoặc Firefox đời gần đây |
| Máy khác trong cùng mạng | Chỉ cần khi muốn chơi với người khác từ máy tính/điện thoại khác |
| Kết nối Internet | Vue và SignalR client được tải từ CDN |

### Chạy tại máy

Mở terminal tại thư mục gốc chứa file `.sln`:

```bash
dotnet run --project 23DTHD6_DemoBanCo
```

Sau đó mở trình duyệt tại:

- **http://localhost:5263** — HTTP
- **https://localhost:7250** — HTTPS (chạy profile `https`)

> Lần chạy đầu với HTTPS sẽ báo chứng chỉ dev chưa được tin cậy. Chấp nhận cảnh báo trên trình duyệt, hoặc tin cậy chứng chỉ bằng:
> ```bash
> dotnet dev-certs https --trust
> ```

### Chạy cho nhiều máy cùng lúc

Bản chạy mặc định chỉ lắng nghe trên máy local. Để người khác trong cùng mạng truy cập được:

```bash
dotnet run --project 23DTHD6_DemoBanCo --urls "http://0.0.0.0:5263"
```

Sau đó mở `http://<IP-của-máy-bạn>:5263` trên thiết bị khác — ví dụ `http://192.168.1.100:5263`. Cả hai thiết bị phải dùng **cùng một địa chỉ** để vào cùng phòng.

### Chơi thử với hai người

1. Mở hai tab trình duyệt cùng truy cập `http://localhost:5263`.
2. Click một quân rồi click một ô khác để di chuyển — nước đi hiện ở cả hai tab.
3. Gõ tin nhắn ở khung chat rồi nhấn Enter — tin nhắn hiện ở cả hai tab.

### Các lệnh khác

```bash
# Build
dotnet build 23DTHD6_DemoBanCo.sln

# Chạy không mở trình duyệt tự động
dotnet run --project 23DTHD6_DemoBanCo --launch-profile http

# Đóng gói bản chạy thật
dotnet publish 23DTHD6_DemoBanCo -c Release -o ./publish
```

### Cấu trúc thư mục

```
23DTHD6_DemoBanCo.sln
└── 23DTHD6_DemoBanCo/
    ├── Program.cs                 Khởi tạo app, route, SignalR
    ├── Hubs/
    │   └── ChessHub.cs            Relay tín hiệu cho client
    ├── Controllers/
    │   ├── HomeController.cs      Trang Index và Privacy
    │   └── api/
    │       └── BanCoController.cs Trả vị trí 32 quân
    ├── Models/
    │   ├── ChessMove.cs           Dữ liệu một nước đi
    │   └── ErrorViewModel.cs
    ├── Views/
    │   ├── Home/Index.cshtml      Bàn cờ, panel video, chat
    │   └── Shared/_Layout.cshtml  Layout dùng chung
    └── wwwroot/
        ├── co_tuong_initial.json   Vị trí khởi tạo của 32 quân
        ├── js/cotuong.js          Logic client (Vue 3)
        └── images/                Ảnh nền và 32 hình quân SVG
```

---

## 3. Trạng thái hiện tại

Phần này mô tả đúng những gì đang hoạt động và những gì chưa, tính đến lần kiểm tra gần nhất.

### Đã hoạt động

| Tính năng | Ghi chú |
|---|---|
| Render bàn cờ 10×9 | 32 quân đặt đúng vị trí chuẩn, vẽ đè lên ảnh nền bằng CSS |
| Nạp vị trí quân | Đọc từ `co_tuong_initial.json` qua API `GET /api/BanCo/getboard` |
| Đồng bộ nước đi | Chọn quân → click ô đích → gửi qua SignalR → cả hai tab cập nhật giống nhau |
| Chat trong phòng | Nhấn Enter để gửi, tự cuộn xuống tin nhắn mới nhất |
| Cơ chế phòng | `JoinRoom` / `LeaveRoom` có hoạt động thật trên SignalR |

### Chưa hoạt động

| Tính năng | Nguyên nhân |
|---|---|
| **Gọi video** | Client gọi `SendOffer` và `SendAnswer` nhưng `ChessHub` **chưa có** hai method này. Bấm "Gọi video" luôn báo `HubException: Method does not exist` |
| **Cách ly phòng** | Cả ba chỗ phát tin đều đang dùng `Clients.All` (phần `.Group(roomId)` bị comment). Mọi người kể cả ở phòng khác đều nhận tin — đây là lỗi rò rỉ dữ liệu, không chỉ là thiếu tính năng |
| **Tự kết nối lại phòng** | Client gọi `connection.start()` mà không `await`, nên `JoinRoom` hay báo lỗi. Với `withAutomaticReconnect()`, sau khi mất kết nối client không vào lại phòng và không kết nối lại được |

### Chưa được xử lý

Những phần sau **chưa có trong mã nguồn**, không phải lỗi mà là chưa xây:

- **Luật cờ tướng** — không có kiểm tra nước đi. Bất kỳ ai cũng có thể di chuyển quân bất kỳ đến bất kỳ ô nào, kể cả ngoài bàn cờ. Không có luật bắt quân, tượng, pháo, mã, xe.
- **Phân biệt phe / lượt đi** — không có khái niệm quân đen hay đỏ, không có lượt. Người chơi có thể cầm quân của cả hai phe và đi liên tục.
- **Chống gian lận** — server tiếp nhận và phát lại mọi nước đi mà không kiểm tra gì. Trạng thái ván đấu nằm ở client, tự do sửa bằng DevTools.
- **Bắt quân** — nhánh xử lý trong `cotuong.js` có sẵn nhưng không bao giờ chạy được, vì mọi quân đều chặn sự kiện click ở cấp ô.
- **Lưu ván / người chơi** — không lưu lịch sử, không đăng nhập, không gán danh tính.
- **Nhiều người cùng phòng** — chưa giới hạn số người vào phòng.
- **Kiểm thử** — chưa có test nào.
- **`README`/`git`** — trước khi có tài liệu này, thư mục chưa được khởi tạo Git và thiếu `.gitignore`.

### Lỗi giao diện

- Link CSS scoped trong `_Layout.cshtml` trỏ tới tên file không tồn tại, dẫn tới lỗi 404. Lỗi này vẫn xảy ra với bản `publish` dù chạy thật, không chỉ khi phát triển.
- Panel video dùng các lớp CSS chưa được định nghĩa ở bất kỳ đâu, nên hiển thị chồng lên khung chat.
- Một số đoạn CSS bị khai báo trùng lặp trong `Index.cshtml`.

### Thứ tự nên làm tiếp

1. Bổ sung `SendOffer` / `SendAnswer` vào `ChessHub` — mở khoá tính năng gọi video đang chết.
2. Bật `.Group(roomId)` — sửa lỗi rò rỉ tin nhắn giữa các phòng.
3. Sửa `await connection.start()` và thêm `onreconnected` để vào lại phòng.
4. Xoá handler `ReceiveOffer` bị đăng ký trùng trong `cotuong.js`.
5. Sửa link CSS và bổ sung CSS cho panel video.
6. Chuyển trạng thái ván đấu về phía server: vị trí quân, phe, lượt đi, luật cờ.
7. Khởi tạo Git và thêm `.gitignore` trước khi phát triển tiếp.
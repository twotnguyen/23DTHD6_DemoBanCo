# 23DTHD6 — Cờ Tướng Online (Xiangqi)

Game cờ tướng online nhiều người chơi, xây dựng cho đồ án môn **QL-DA-CNTT**.

Mỗi người chơi mở một tab trình duyệt, di chuyển quân và chat trong cùng một phòng.
Trạng thái ván đấu nằm trên **máy chủ**; client chỉ gửi ý định đi cờ và nhận lại
thế cờ đã được kiểm tra — không có đường nào để sửa thế cờ bằng DevTools.

---

## 1. Tổng quan

| Hạng mục | Giá trị |
|---|---|
| Framework | ASP.NET Core MVC 8.0 (`net8.0`) |
| Giao tiếp thời gian thực | SignalR (WebSocket) — 4 hub |
| Cơ sở dữ liệu | EF Core 8 + SQLite, tự áp dụng migration khi khởi động |
| Frontend | JavaScript thuần, không cần build step |
| Quản lý phiên | Cookie đăng nhập + Session cho tiến trình đăng ký/OTP |
| Package NuGet | 2 (`EntityFrameworkCore.Sqlite`, `EntityFrameworkCore.Design`) |

### Vì sao chọn công nghệ này

- **ASP.NET Core 8 MVC** — phần render trang và phục vụ `wwwroot`, tích hợp DI sẵn có.
- **SignalR** — kênh hai chiều có group, tự kết nối lại, và nhận diện người nhận
  theo claim. Ba tính chất này là bắt buộc vì mọi luật của hệ thống (cấm xem ván
  xếp hạng, chỉ đấu thủ mới đi được, chỉ khán giả mới bị đuổi) đều gắn với *người
  nhận*, chứ không gắn với nội dung tin.
- **EF Core + SQLite** — mô hình quan hệ rõ ràng (người, phòng, ván, nước đi, chat)
  và cần truy vấn theo quan hệ để tính Elo, kiểm tra quyền vào phòng.
- **JavaScript thuần** — `chess-board.js` và `audio.js` là hai module độc lập, nối vào
  nhau bằng `window`, không phụ thuộc framework. Đổi lại: không có type-checking.

### Vai trò từng thành phần

| Thành phần | Trách nhiệm |
|---|---|
| `Program.cs` | Đăng ký DI, cookie, session, 4 hub, áp dụng migration |
| `Models/` | Thực thể CSDL và các enum nghiệp vụ |
| `Data/AppDbContext.cs` | 11 `DbSet`, ràng buộc unique và cascade |
| `Services/ChessRules.cs` | Luật đi của 7 loại quân |
| `Services/CheckDetector.cs` | Phát hiện chiếu tướng |
| `Services/GameEvaluator.cs` | Đọc/ghi FEN, ký hiệu nước đi, phân định kết quả ván |
| `Services/AiEngine.cs` | Minimax + Alpha-Beta, 3 cấp độ theo độ sâu và ngân sách thời gian |
| `Services/MatchService.cs` | Vòng đời ván: nước đi, đi lại, hoà, tái đấu, hết giờ, treo ván, Elo |
| `Services/RoomService.cs` | Vòng đời phòng: ghế, mã, chế độ, chặn, chuyển quyền, đóng phòng |
| `Services/MatchmakingQueue.cs` | Hàng đợi ghép trận trong RAM (singleton) |
| `Services/BadWordFilter.cs` | Che từ cấm bằng `***` |
| `Hubs/ChessHub.cs` | Kênh realtime phòng chơi và ván đấu |
| `Hubs/MatchmakingHub.cs` | Hàng đợi ghép trận tự do và xếp hạng |
| `Hubs/AiHub.cs` | Ván đấu với máy + số liệu tìm kiếm |
| `Hubs/SocialHub.cs` | Kết bạn, nhắn tin riêng, mời vào phòng |
| `wwwroot/js/chess-board.js` | Vẽ bàn cờ, click-to-move, kéo-thả, ô tô |
| `wwwroot/js/audio.js` | Âm thanh tổng hợp bằng Web Audio API |

---

## 2. Cách chạy

### Yêu cầu

| Yêu cầu | Ghi chú |
|---|---|
| [.NET SDK 8.0](https://dotnet.microsoft.com/download) | Kiểm tra bằng `dotnet --version` |
| Trình duyệt | Chrome, Edge hoặc Firefox đời gần đây |

### Chạy tại máy

```bash
dotnet run --project 23DTHD6_DemoBanCo
```

Mở trình duyệt tại **http://localhost:5263**.

Database SQLite được tạo và áp dụng migration tự động lần chạy đầu, không cần
lệnh cài đặt thủ công.

### Chạy cho nhiều máy trong mạng LAN

```bash
dotnet run --project 23DTHD6_DemoBanCo --urls "http://0.0.0.0:5263"
```

Mở `http://<IP-của-máy-bạn>:5263` trên thiết bị khác — ví dụ `http://192.168.1.100:5263`.

### Chơi thử với hai người

Trước hết cần **hai tài khoản khác nhau**. Cách nhanh nhất cho buổi demo:

1. Đăng ký ở cửa sổ ẩn danh đầu tiên, copy cookie đăng nhập ra.
2. Đăng ký ở cửa sổ thường, vào **Sảnh chơi → Đánh thường → Ghép ngẫu nhiên**
   ở cả hai cửa sổ.

Hoặc không cần đăng nhập trước:

1. Cả hai cùng bấm **Ghép ngẫu nhiên** ở tab Đánh thường.
2. Khi tìm thấy nhau, cả hai tự chuyển vào phòng chờ, đồng ý **Sẵn sàng**,
   đồng hồ đếm **3-2-1** rồi vào ván.

### Tài khoản thử nghiệm có sẵn

| Tài khoản | Mật khẩu | Ghi chú |
|---|---|---|
| `nguyen_a`, `twot`, `twotnguyen` | xem trong lần khởi tạo đầu | Tài khoản có sẵn |

Mã OTP ở bản demo **hiển thị thẳng trên màn hình** bước 3 thay vì gửi mail, vì dự án
không cấu hình SMTP. Thời hạn 3 phút và giới hạn 5 lần sai vẫn được áp dụng đầy đủ.

### Các lệnh khác

```bash
# Build
dotnet build 23DTHD6_DemoBanCo.sln

# Chạy không mở trình duyệt tự động
dotnet run --project 23DTHD6_DemoBanCo --launch-profile http

# Đóng gói bản chạy thật
dotnet publish 23DTHD6_DemoBanCo -c Release -o ./publish
```

---

## 3. Tính năng

### 3.1 Tài khoản

- Đăng ký 3 bước: **Username + Mật khẩu → Email → OTP**, tự đăng nhập khi xác minh
  xong và vào thẳng sảnh.
- OTP hết hạn sau **3 phút**, sai quá **5 lần** thì huỷ mã, nút gửi lại đếm lùi 60 giây.
- Đổi Username qua quy trình 4 bước có OTP; **Email khoá cứng**, không đổi được.
- **Chế độ Khách**: vào chơi ngay bằng tên hiển thị tạm, không tính Elo, không
  lưu lịch sử, không được vào Xếp hạng.
- `Username` (không dấu, 3–20 ký tự, duy nhất) tách khỏi `Display Name`
  (tiếng Việt có dấu, 2–30 ký tự). Đổi Display Name tự do, không cần OTP.

### 3.2 Ba chế độ chơi

| Chế độ | Ghép trận | Khán giả | Undo | Thời gian | Elo |
|---|---|---|---|---|---|
| **Đánh thường** | Ngẫu nhiên hoặc tạo phòng | Có, tối đa 5 | Tối đa 3 lượt/bên | Không giới hạn / 5 / 10 / 15 phút | Không |
| **Đánh hạng** | Ngẫu nhiên theo Elo | **Cấm tuyệt đối** | **Cấm** | Cố định 10 phút | Có |
| **Đánh với máy** | — | — | Tối đa 3 lượt, không cần xin phép | Không giới hạn | Không |

### 3.3 Phòng chờ

Chủ phòng mặc định ngồi ghế **Đỏ**, tự do đổi ghế khi một mình; người vào sau
tự động nhận ghế còn trống. Hai bên bấm **Sẵn sàng** → đồng hồ đếm **3-2-1** →
ván bắt đầu.

- **Xin đổi bên** với hạn chờ 30 giây; đổi xong cả hai phải bấm sẵn sàng lại.
- **Chia sẻ phòng** qua link, mã 8 ký tự và **mã QR**.
- **Mời bạn bè** chỉ khi bạn đang online, lời mời tự hết hạn sau 30 giây.
- Chủ phòng rời đi → quyền tự chuyển cho người còn lại; không còn ai → phòng đóng.
- Chế độ phòng đổi được **ngay cả khi đang đánh**: sang *Khoá* thì ẩn khỏi sảnh
  và chặn lượt vào mới, nhưng khán giả đang ở trong vẫn xem tiếp.

### 3.4 Ván đấu

- Luật cờ tướng đầy đủ do máy chủ kiểm tra: cản chân Mã, mắt Tượng, ngòi Pháo,
  cấm tự chiếu và cấm để hai tướng nhìn nhau.
- Kết quả ván: chiếu hết (thua), vây khốn (thua), lặp thế 3 lần (hoà), đầu hàng
  (thua), hết giờ (thua), mất kết nối quá 60 giây (thua), treo ván (thua).
- **Xin hoà** và **xin đi lại** đều có hạn chờ 30 giây, tự huỷ nếu đối thủ không
  trả lời — kể cả khi họ đóng tab.
- Mất kết nối được ân hạn **60 giây** trước khi xử thua.
- **Tái đấu**: sau khi ván xong, phòng giữ trạng thái 10 phút; hai bên cùng bấm
  thì tạo ván mới và **tự đổi bên**.
- Đồng hồ chạy trên máy chủ, truyền xuống mỗi giây; client chỉ hiển thị.

### 3.5 Chat

- Hai kênh: **Kênh Riêng** (chỉ hai đấu thủ) và **Kênh Chung** (có khán giả).
  Khán giả **chỉ thấy Kênh Chung** vì không có quyền đọc kênh riêng.
- **12 sticker cảm xúc nhanh** bấm một chạm là gửi.
- Từ cấm bị che bằng `***` ở mọi kênh.

### 3.6 Đấu với máy

Chọn cấp độ **Dễ / Trung bình / Khó** và cầm quân **Đỏ / Đen / Ngẫu nhiên**.
Máy chơi bằng Minimax + Alpha-Beta tự viết, không gọi API bên ngoài:

| Cấp độ | Độ sâu | Ngân sách thời gian |
|---|---|---|
| Dễ | 2 | 300 ms |
| Trung bình | 4 | 1000 ms |
| Khó | 6 | 3000 ms |

- **Không có gợi ý nước đi** — giữ đúng đối kháng thuần tuý.
- Đi lại tối đa **3 lượt**, mỗi lượt lùi trọn **1 cặp nước đi** (nước máy + nước
  người chơi) nên lượt đi luôn trả về cho người chơi.
- Ván được lưu vào lịch sử kèm nhãn 🤖 và cấp độ, xem lại được từng nước.

### 3.7 Xếp hạng Elo

Công thức chuẩn FIDE, khởi đầu **1200**:

$$E_A = \frac{1}{1 + 10^{(R_B - R_A)/400}} \qquad R'_A = R_A + K\,(S_A - E_A)$$

$K = 32$ cho 30 ván đầu (giai đoạn định vị trình độ), $K = 16$ từ ván thứ 31.

| Bậc | Elo |
|---|---|
| Kỳ thủ mới | < 1200 |
| Sơ cấp | 1200 – 1399 |
| Trung cấp | 1400 – 1599 |
| Cao cấp | 1600 – 1799 |
| Kiện tướng | 1800 – 1999 |
| Đại sư | ≥ 2000 |

Bảng xếp hạng hiện **Top 50** kèm dòng cá nhân ghim ở chân bảng. Ghép trận mở
rộng biên chênh lệch **±100 điểm**, mỗi 10 giây chờ thêm **±50**.

### 3.8 Bạn bè và tin nhắn riêng

- Kết bạn theo tên đăng nhập; trang bạn bè hiện trạng thái **Đang online /
  Ngoại tuyến** của từng người.
- Nhắn tin riêng **chỉ với bạn bè đã chấp nhận** — luật này kiểm ở server, gõ tay
  URL cũng không xem được lịch sử của người lạ.
- Khung chat có **badge chưa đọc** trên thanh điều hướng, hỗ trợ đủ 12 sticker.

### 3.9 Xem lại ván

Lịch sử ván đấu, xem lại từng nước với nút tua, và **Sao chép FEN** +
**Tải PGN** để đưa sang engine khác phân tích.

### 3.10 Công cụ demo

- **Thông số máy cờ** (ván với máy): số thế cờ đã duyệt, độ sâu đạt được, thời gian
  tính, nước đi tối ưu — số liệu thật lấy từ lượt tìm kiếm vừa rồi.
- **Mô phỏng mất mạng** (phím tắt `Ctrl+Shift+D`): ngắt kết nối để trình diễn cơ
  chế ân hạn 60 giây, rồi kết nối lại và tự đồng bộ lại bàn cờ.

---

## 4. Kiến trúc và quyết định đã chốt

### Máy chủ là nguồn chân lý

Client **chỉ gửi ý định** (`fromRow/fromCol/toRow/toCol`). Máy chủ kiểm tra lại
toàn bộ luật rồi mới ghi và phát thế cờ mới cho cả ván. Hệ quả: sửa JavaScript ở
DevTools không làm được nước đi không hợp lệ.

### Trạng thái phụ thuộc người nhận thì phải gửi riêng

`yourSide`, `canMove` và `undoLeft` khác nhau giữa hai đấu thủ. Nếu gộp chúng vào
một `Clients.Group(...).SendAsync`, cả hai sẽ nhận cùng một giá trị — tức là cùng
tưởng mình cầm cùng một phe, và người không đến lượt sẽ bị khoá bàn vĩnh viễn.
Vì vậy phần dùng chung (FEN, nước hợp lệ, chiếu tướng) dựng một lần, còn phần riêng
gửi riêng cho từng kết nối.

### Ba đồng hồ 30 giây

Xin đi lại, xin hoà và xin đổi bên đều có hạn. Hạn được **đồng hồ nền** theo dõi chứ
không tin phía client, vì người nhận có thể đóng tab và không gửi được câu trả lời.
Đề nghị bị từ chối hoặc hết hạn thì **không trừ lượt**.

### Đếm ngược 3-2-1 khoá bàn thật

Trong lúc đếm, máy chủ **không gửi danh sách nước hợp lệ** và chưa trừ đồng hồ.
Đồng hồ nền phát `MatchCountdownOver` khi hết 3 giây để mở khoá bàn — nếu thiếu
bước này, sự kiện duy nhất lúc tạo ván vẫn mang danh sách rỗng và cả hai bên bị
khoá bàn vĩnh viễn.

### Phòng có vòng đời thật

`RoomStatus.Closed` khác `Locked`: *Khoá* là trạng thái tạm của chủ phòng, còn
*Đã đóng* là kết thúc vòng đời phòng. Phòng đóng thì **danh sách chặn cũng hết
hiệu lực** và không ai vào lại được, kể cả bằng link cũ.

### Truyền trạng thái sau khi mở trang

Ván với máy được tạo ở sảnh rồi mới chuyển sang trang đấu; ván PvP có thể đã đi
vài nước khi người chơi bấm F5. Cả hai trường hợp đều **không có sự kiện nào phát
lại**, nên trang gọi `SyncMatch` / `GetRoomState` để lấy đúng thế cờ kèm nước hợp
lệ thay vì dựa vào việc "chắc là có sự kiện đến".

### AI lưu số liệu thật

Cột `AiNodesEvaluated` / `AiDepthReached` / `AiElapsedMs` / `AiPrincipalVariation`
được ghi vào `MatchMoves` ngay sau mỗi lượt máy đi. Widget thống kê đọc thẳng từ đó
nên báo cáo đúng lượt tìm kiếm **đã diễn ra**, không phải số ước lượng chạy lại.

---

## 5. Cấu trúc thư mục

```
23DTHD6_DemoBanCo.sln
└── 23DTHD6_DemoBanCo/
    ├── Program.cs                 Khởi động app, DI, cookie, 4 hub
    ├── Data/AppDbContext.cs       11 DbSet + ràng buộc
    ├── Models/                    Thực thể và enum
    ├── Migrations/                Lịch sử migration EF Core
    ├── Services/                  Toàn bộ nghiệp vụ (20 file)
    ├── Hubs/                      ChessHub, MatchmakingHub, AiHub, SocialHub
    ├── Controllers/
    │   ├── AccountController.cs   Đăng nhập, đăng ký 3 bước, OTP, hồ sơ, đổi tên
    │   ├── RoomController.cs      Sảnh, tạo phòng, phòng chờ, QR, đuổi, ván máy
    │   ├── ProfileController.cs   Bảng xếp hạng, lịch sử ván đấu
    │   ├── SocialController.cs    Bạn bè, tin nhắn riêng
    │   ├── ReplayController.cs    Xem lại ván, xuất PGN
    │   └── api/BanCoController.cs Thế cờ khởi tạo
    ├── Views/
    │   ├── Account/               Đăng nhập, đăng ký, OTP, hồ sơ, đổi tên
    │   ├── Room/                  Lobby, Waiting (phòng chờ + thi đấu), AiGame
    │   ├── Profile/               Leaderboard, MatchHistory
    │   ├── Social/                Friends, Messages
    │   ├── Replay/                Trình xem lại ván
    │   └── Shared/                Layout, view component badge tin chưa đọc
    └── wwwroot/
        ├── js/chess-board.js      Module vẽ bàn cờ (không phụ thuộc framework)
        ├── js/audio.js            Âm thanh Web Audio API tổng hợp
        ├── images/                Ảnh nền bàn + 32 quân SVG chữ Hán
        └── lib/                   Bootstrap, jQuery (bản cục bộ)
```

---

## 6. Trợ năng

- Quân có **viền phân biệt phe** không phụ thuộc màu hoa văn, cho người mù màu.
- Nhãn văn bản rõ ràng cho trạng thái, lượt đi và bậc xếp hạng.
- Bàn cờ hỗ trợ cả **bấm-để-đi** lẫn **kéo-thả** trên chuột và cảm ứng.

## 7. Ngoài phạm vi

Hệ thống **giải đấu** (chia bảng, nhánh knockout) nằm ngoài phạm vi đã chốt.
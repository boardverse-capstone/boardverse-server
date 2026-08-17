

# BoardVerse – Nghiệp vụ hệ thống đặt cọc, tạo lobby, giữ chỗ và quản lý BVC

> **Mục đích tài liệu**
>
> File này là **nguồn nghiệp vụ chính thức (single source of truth)** cho cả frontend (Flutter Mobile) và backend (Web API) khi triển khai luồng: **Top-up BVC → Cấu hình lobby → Đặt cọc & giữ chỗ → Tuyển người chơi → Xác nhận booking → Check-in tại quán**.
>
> Mọi quyết định trong tài liệu này đã được chốt với giảng viên hướng dẫn. Mọi thay đổi phải được cập nhật đồng thời tại file này, file thiết kế feature và tài liệu API.

---

## 1. Bối cảnh và vấn đề

### 1.1. Bối cảnh BoardVerse

BoardVerse là nền tảng vận hành và matchmaking cho các quán board game. Hệ thống gồm ba sản phẩm:

- Mobile App cho player.
- Web POS & Management cho quán.
- Web Admin cho quản trị viên và ban tổ chức giải đấu.

Về phía player, nghiệp vụ cốt lõi là: khám phá game → ghép đội → đặt chỗ → chơi tại quán → đánh giá Karma sau phiên.

### 1.2. Vấn đề của luồng cũ

Luồng cũ mô tả trong `lobby_management/docs.md`, `booking_payment/docs.md`, `matchmaking_discovery/docs.md`:

```
Cấu hình lobby
→ Tạo lobby (POST /api/v1/lobbies)
→ Lobby tuyển người (SignalR)
→ Lobby đầy maxPlayers
→ Auto-create booking (POST /api/v1/lobbies/{id}/lock)
→ BookingSummaryPage
→ Thanh toán cọc qua gateway
→ Confirm booking
```

Hai vấn đề được giảng viên chỉ ra:

1. **Không giữ chỗ sớm**: nhiều nhóm tạo lobby đồng thời; đến khi đủ người mới gọi API tạo booking, hệ thống phát hiện hết chỗ. Trải nghiệm rất tệ.
2. **Nếu giữ chỗ miễn phí**: player có thể spam tạo lobby để chiếm công suất của quán, ảnh hưởng doanh thu.

Hơn nữa, nếu đã thanh toán cọc qua gateway ngân hàng rồi mới phải refund, nghiệp vụ sẽ phức tạp vì phải xử lý refund qua VNPay/MoMo, đối soát, trừ phí.

### 1.3. Hướng giải quyết được chốt

Thay vì tạo lobby trước rồi đặt cọc sau, BoardVerse chuyển sang mô hình:

```
Cấu hình lobby
→ Tính tiền cọc (BVC) theo maxPlayers
→ Kiểm tra số dư BVC của host
→ Nếu thiếu: Top-up BVC bằng tiền thật
→ Giữ BVC + Giữ chỗ ngồi + Giữ 1 game copy (atomic)
→ Tạo lobby (lobby chỉ tồn tại sau khi giữ chỗ thành công)
→ Lobby tuyển người
→ Đạt minPlayers trước deadline → Xác nhận booking
```

Lợi ích:

- Không còn race condition khi nhiều nhóm cùng đặt.
- Hạn chế spam vì mỗi lobby active yêu cầu tiền cọc thực sự.
- Refund nhanh vì chỉ cần hoàn BVC vào ví.
- Trải nghiệm người dùng ổn định: đã đặt cọc nghĩa là đã giữ chỗ.

---

## 2. BVC – Đơn vị tiền ảo trong ứng dụng

### 2.1. Tên gọi

- **Tên**: BVC (viết tắt của BoardVerse Coin).
- **Tên hiển thị**: **BVC**.
- **Không gọi là "tiền ảo"** hay "token" trên UI để tránh liên tưởng đến cryptocurrency.

### 2.2. Tỷ lệ quy đổi

- **1 BVC = 1.000 VND**.
- Tỷ lệ này **cố định toàn hệ thống**. Không cho phép:
  - Cafe cấu hình lại tỷ lệ.
  - Admin thay đổi tỷ lệ theo thời gian.
  - Tạo số BVC lẻ (0.5 BVC, 1.25 BVC).

### 2.3. Quy tắc sử dụng BVC

| Quy tắc | Mô tả |
|---------|--------|
| Không rút | BVC không thể quy đổi ngược về tiền mặt. |
| Không chuyển nhượng | Player không thể chuyển BVC cho player khác. |
| Chỉ dùng trong hệ thống | BVC chỉ thanh toán các dịch vụ của BoardVerse. |
| Không phải crypto | BVC không phải tiền mã hóa, không có blockchain, không có tỷ giá biến động. |
| Không bonus top-up trong MVP | 100.000 VND → 100 BVC. Không tặng thêm BVC. |
| Refund bằng BVC | Mọi khoản hoàn cọc trả về ví BVC của host. |
| Lưu vết đầy đủ | Mọi thay đổi số dư đều lưu vào ledger (xem mục 7). |

### 2.4. Hiển thị trên UI

Mọi màn hình liên quan tiền phải hiển thị theo cùng cách:

**Màn hình nạp / rút / thanh toán** (hiển thị cả VND):

```
Bạn thanh toán: 100.000 VND
Bạn nhận được: 100 BVC
```

**Màn hình lobby / booking thường** (chỉ BVC):

```
Tiền cọc: 120 BVC
```

**Màn hình hoàn cọc**:

```
Đã hoàn 120 BVC vào ví của bạn.
Tương đương 120.000 VND.
```

### 2.5. Các gói nạp đề xuất

- 20.000 VND → 20 BVC.
- 50.000 VND → 50 BVC.
- 100.000 VND → 100 BVC.
- 200.000 VND → 200 BVC.
- 500.000 VND → 500 BVC.

Cho phép nhập số tiền tùy chỉnh với điều kiện:

- Tối thiểu 10.000 VND (10 BVC).
- Bội số của 1.000 VND.
- Ví dụ: 35.000 VND → 35 BVC. Không nhận 35.500 VND.

---

## 3. Ví BVC và sổ cái (Ledger)

### 3.1. Cấu trúc số dư

Mỗi player có **hai số dư BVC riêng biệt**:

- `availableBalance`: số BVC có thể dùng để đặt cọc hoặc thanh toán.
- `heldBalance`: số BVC đang bị giữ cho một reservation/lobby.

Ví dụ: player có 200 BVC và đặt cọc 120 BVC:

```
availableBalance = 80 BVC
heldBalance     = 120 BVC
```

### 3.2. Vòng đời của một khoản BVC

```
TOP_UP              : availableBalance += amount
DEPOSIT_HOLD        : availableBalance -= amount; heldBalance += amount
DEPOSIT_RELEASE     : heldBalance -= amount; availableBalance += amount
DEPOSIT_CAPTURE     : heldBalance -= amount; settlement += amount (chuyển cho quán)
DEPOSIT_FORFEIT     : heldBalance -= amount; forfeit += amount (chuyên cho quán khi no-show)
```

### 3.3. Sổ cái (Ledger)

Mọi thay đổi số dư phải được ghi vào bảng ledger với các trường:

- `id` (UUID).
- `userId`.
- `type` (một trong các loại ở mục 3.2).
- `amount` (luôn dương, BVC).
- `relatedBookingId` hoặc `relatedLobbyId` (nếu có).
- `relatedPaymentRef` (nếu là TOP_UP, tham chiếu gateway).
- `idempotencyKey` (để chống trùng yêu cầu).
- `createdAt`.
- `balanceSnapshot` (số dư availableBalance sau giao dịch, để debug).

**Quy tắc ledger**:

- Không bao giờ UPDATE hoặc DELETE một dòng ledger.
- Sửa chữa sai phải tạo một ledger entry mới loại `ADJUSTMENT`.
- `availableBalance` và `heldBalance` của user được tính lại từ ledger khi cần kiểm tra, hoặc cache và verify định kỳ.

---

## 4. Quy tắc nghiệp vụ chi tiết

### 4.1. Người trả cọc

**BR-DEPOSIT-01**: Toàn bộ tiền cọc do **host** thanh toán.

- Thành viên tham gia lobby không phải trả cọc.
- Mọi khoản hoàn cọc chỉ trả về ví của host.
- Nếu thành viên rời lobby, host không được hoàn một phần cọc.

### 4.2. Số ghế được giữ

**BR-RESERVATION-01**: Khi tạo lobby, hệ thống giữ **`maxPlayers` ghế**.

Ví dụ lobby hỗ trợ 3–6 người và host chọn `maxPlayers = 6` thì hệ thống giữ 6 ghế ngay khi đặt cọc thành công.

Lý do:

- Bảo đảm bất kỳ ai join lobby cũng có chỗ.
- Không xảy ra tình trạng lobby hiển thị còn slot nhưng không thể vào.
- Đơn giản hơn so với việc tăng/giảm ghế theo từng lượt join.

Để tránh giữ thừa tài nguyên, kết hợp với:

- Giới hạn lobby active (mục 4.8).
- Deadline tuyển người (mục 4.6).

### 4.3. Giữ game copy

**BR-RESERVATION-02**: Khi tạo lobby, hệ thống giữ **1 bản copy** của game được chọn.

- Quán có 3 hộp Catan = 3 game copy.
- Lobby Catan cần giữ 1 copy trong khung giờ dự kiến.
- Mã hộp/barcode cụ thể có thể gán lúc check-in; trước đó backend chỉ cần khóa 1 đơn vị tồn kho game.

Nếu không giữ game copy:

- Lobby đủ người nhưng khi đến quán thì game đã được nhóm khác sử dụng.
- Trải nghiệm người dùng xấu tương tự hết ghế.

### 4.4. Công thức tính tiền cọc

**BR-DEPOSIT-02**: Tiền cọc được tính theo công thức:

```
depositAmount = depositRatePerPerson × maxPlayers
```

Trong đó:

- `depositRatePerPerson` do **cafe cấu hình** (tính bằng BVC/người).
- `maxPlayers` do host cấu hình khi tạo lobby.

**BR-DEPOSIT-03**: Hệ thống giới hạn:

- `depositRatePerPerson` tối thiểu **1 BVC/người**.
- `depositRatePerPerson` tối đa **100 BVC/người** (configurable).
- `depositAmount` không vượt quá **50% giá trị giờ chơi đầu tiên** (BR-03 hiện tại).
- Hệ thống tự áp dụng **hệ số rủi ro** (mục 4.9) để điều chỉnh cọc của user.

Ví dụ:

```
Cafe cấu hình: 20 BVC/người
Host tạo lobby: maxPlayers = 6
Cọc cơ bản: 20 × 6 = 120 BVC
Hệ số rủi ro: 1.0
Cọc cuối: 120 BVC
```

### 4.5. Hoàn cọc khi không đủ người

**BR-REFUND-01**: Nếu lobby không đạt `minPlayers` trước deadline:

- Lobby chuyển trạng thái `timeoutFailed`.
- Tự động giải phóng chỗ ngồi và game copy đã giữ.
- Hoàn **100% BVC** về `availableBalance` của host.
- Không refund về nguồn tiền thật.
- Không phạt Karma trong lần đầu thất bại.

### 4.6. Deadline tuyển người

**BR-LOBBY-01**: Lobby có `recruitmentDeadline`, được tính từ `playDate` + `timeSlot`:

```
recruitmentDeadline = (playDate + timeSlot.startTime) - leadTimeMinutes
```

Trong đó:

- `playDate`: ngày dự kiến chơi (chỉ ngày, không có giờ), do host chọn.
- `timeSlot`: một trong các khung giờ cố định (mục 4.19), do host chọn.
- `leadTimeMinutes` lấy từ deposit config của cafe (mặc định **20 phút**).

Khi đến `recruitmentDeadline`:

| Điều kiện | Xử lý |
|-----------|--------|
| `currentPlayers >= minPlayers` | Booking chuyển `confirmed`. Lobby tiếp tục nhận người đến `maxPlayers`. |
| `currentPlayers < minPlayers` | Lobby timeout. Hoàn 100% BVC cho host. |
| `currentPlayers == maxPlayers` | Đóng tuyển sớm. Booking chuyển `confirmed`. |

**BR-LOBBY-01a (Buffer tối thiểu)**: `recruitmentDeadline - now() ≥ 120 phút`.

Lý do:

- Đủ thời gian cho notification, chia sẻ link, bạn bè thấy và join.
- Tránh lobby fail ngay vì thiếu thời gian tuyển.
- Áp dụng khi host chọn slot gần nhất có thể.

**BR-LOBBY-01b (Từ chối cứng khi buffer quá ngắn)**: Nếu `recruitmentDeadline - now() < 60 phút`, hệ thống **từ chối tạo lobby**, yêu cầu host chọn `timeSlot` xa hơn.

Lý do:

- Tránh lobby thất bại chắc chắn.
- Buộc host chủ động chọn slot xa hơn.

**BR-LOBBY-01c (Cảnh báo khi buffer vừa đủ)**: Nếu `60 phút ≤ buffer < 120 phút`, hiển thị cảnh báo UI nhưng vẫn cho phép tạo. Host tự chịu trách nhiệm.

Chi tiết về `timeSlot`, bảng hạn mức theo khoảng cách và công thức deadline xem **mục 4.19** và **mục 17 (Recruitment Window & Spam Prevention)**.

### 4.7. Đạt `minPlayers` hay `maxPlayers` mới xác nhận?

**BR-LOBBY-02**: Chỉ cần đạt `minPlayers` là đủ điều kiện xác nhận booking.

- Ví dụ game hỗ trợ 3–6 người, `minPlayers = 3`.
- Lobby đạt 3 người trước deadline: booking được xác nhận, vẫn có thể tuyển thêm đến 6.
- Lobby đạt 6 người trước deadline: đóng tuyển sớm, booking xác nhận.
- Không bắt buộc phải đầy `maxPlayers`.

Lý do: nhiều game hoàn toàn có thể chơi với `minPlayers`. Nếu bắt đầy `maxPlayers`, nhiều lobby hợp lệ sẽ bị hủy oan.

### 4.8. Giới hạn lobby/reservation active của một user

**BR-USER-LIMIT-01**:

- Một user chỉ được là **host của tối đa 1 lobby active** tại một thời điểm.
- Cùng lúc đó, user có thể tham gia tối đa **1 lobby khác** (với vai trò member).
- Tổng cộng user không xuất hiện trong quá **2 lobby active**.
- Khi lobby do user host chấm dứt (hết hạn, hoàn thành, bị hủy), user mới được tạo lobby mới.

**BR-USER-LIMIT-02**: Không cho phép **lịch chồng lấn**.

Hai lobby/booking được coi là chồng lấn nếu khoảng thời gian giao nhau, tính cả thời lượng dự kiến và đệm 30 phút:

```
[startTime, endTime + 30 phút]
```

Quy tắc:

- Host không được tạo lobby chồng với lobby/booking hiện có.
- Member không được join lobby chồng với lobby/booking hiện có.
- Tất cả các API tạo/join đều phải validate overlap.

**BR-USER-LIMIT-03 (Cap tổng cọc active)**: Tổng giá trị `heldBalance` của các lobby ACTIVE của 1 user không vượt quá:

- **500.000 BVC** cho user thường.
- **1.000.000 BVC** cho user VIP.
- **200.000 BVC** cho user có `riskMultiplier` ≥ 1.25.

Nếu tổng cọc vượt cap: từ chối tạo lobby mới, yêu cầu đợi lobby cũ terminal.

Lý do:

- Chống spam cọc chiếm chỗ trên diện rộng.
- Phải đợi lobby cũ terminal (timeout/hủy/xong) mới có thêm slot cọc.

**BR-USER-LIMIT-04 (Cross-role: player không được host)**: User đang là **member** của một lobby ACTIVE thì **không được tạo lobby mới** với vai trò host.

Áp dụng cho đến khi lobby mà user đang là member:

- Đạt terminal state (`completed`, `timeoutFailed`, `hostCancelled`, `cancelledByCafe`, `noShow`).
- User rời lobby thành công (chỉ áp dụng khi lobby còn recruitmentDeadline).

Lý do:

- Tránh 1 user "ôm" nhiều lobby cùng lúc (1 host + 1 member → 2 lobby).
- Tránh tình trạng user cam kết chơi 2 nơi mà không thể đến cả 2.

**BR-USER-LIMIT-05 (Cross-role: host được phép join)**: **ĐÃ BỎ — User đang là host của một lobby ACTIVE THÌ ĐƯỢC PHÉP join lobby khác với vai trò member.**

Điều kiện để host join lobby khác:

| Điều kiện | Giải thích |
|---|---|
| (a) Không overlap | Không trùng lịch với lobby đang host + buffer 30 phút (BR-USER-LIMIT-02) |
| (b) Tổng lobby | host + member ≤ 2 (BR-USER-LIMIT-01) |
| (c) Không phải cooling-off | User không đang trong thời gian cooling-off (BR-NEW-10) |

**Ví dụ hợp lệ:**

- Host lobby thứ 7 (3 ngày sau) → Join lobby hôm nay (khác khung giờ, không overlap) ✓
- Host lobby tối mai (2 ngày sau) → Join lobby sáng mai (khác ngày) ✓
- Host lobby 14:00 ngày mai → Join lobby 19:00 ngày mai (khác khung giờ) ✓

**Ví dụ bị từ chối:**

- Host lobby 19:00 hôm nay → Join lobby 18:30 hôm nay (overlap) ✗
- Host lobby 19:00 hôm nay → Join lobby 19:00 hôm nay (cùng khung giờ, overlap) ✗
- Host lobby 14:00 ngày mai → Join lobby 14:00 ngày mai (cùng khung giờ, overlap) ✗

**Lý do bỏ luật cũ:**

- BR-USER-LIMIT-02 đã ngăn overlap lịch.
- BR-USER-LIMIT-01 (tổng ≤ 2) đã giới hạn tổng số lobby.
- Luật cũ quá restrictive, không cho phép use case hợp lý (host tổ chức lobby xa mà vẫn muốn chơi gần).

**Bảng tổng hợp BR-USER-LIMIT:**

| BR | Nội dung |
|----|----------|
| BR-USER-LIMIT-01 | Tối đa 1 lobby host active, 1 lobby member active, tổng 2 lobby. |
| BR-USER-LIMIT-02 | Không lịch chồng lấn (overlap + buffer 30 phút). |
| BR-USER-LIMIT-03 | Tổng cọc held ≤ 500k (user thường) / 1tr (VIP) / 200k (risk cao). |
| BR-USER-LIMIT-04 | Player join lobby → không được tạo lobby mới làm host. |
| BR-USER-LIMIT-05 | **ĐÃ BỎ** — Host được phép join lobby khác nếu không overlap. |

Chi tiết các trường hợp cross-role xem **mục 10.11** và **mục 17**.

### 4.9. Hệ số rủi ro cho tiền cọc

**BR-DEPOSIT-04**: Hệ thống tự động áp dụng `riskMultiplier` dựa trên lịch sử của user:

| Hành vi | `riskMultiplier` |
|---------|------------------|
| User bình thường | 1.0 |
| Có 2–3 lobby thất bại trong 30 ngày gần nhất | 1.25 |
| Có ≥ 4 lobby thất bại HOẶC từng no-show | 1.5 |
| Tái diễn nhiều lần | 2.0 (tối đa) |

```
finalDeposit = baseDeposit × riskMultiplier
```

Hệ số này do **hệ thống tính**, không phải do cafe cấu hình.

### 4.10. Chính sách hủy của host

**BR-REFUND-02**: Mức hoàn khi host hủy lobby:

| Thời điểm hủy | Mức hoàn BVC | Ảnh hưởng Karma |
|----------------|--------------|------------------|
| Trước giờ chơi ≥ 24 giờ | 100% | Không phạt |
| Từ 6 giờ đến < 24 giờ trước giờ chơi | 50% | Giảm nhẹ |
| Dưới 6 giờ trước giờ chơi | 0% | Giảm đáng kể |
| Không đến (no-show) | 0% | Giảm nặng, có thể tạm khóa |

**BR-REFUND-03**: Ngoại lệ 15 phút đầu:

- Trong vòng **15 phút** kể từ khi tạo lobby, host được hủy và hoàn 100% BVC.
- Điều kiện: chưa có thành viên nào khác tham gia.
- Đây là "grace period" cho phép host chọn nhầm và sửa lại.

### 4.11. Thành viên rời lobby

**BR-LOBBY-03**:

- Thành viên được rời lobby trước `recruitmentDeadline`.
- Lobby giảm `currentPlayers`, slot được mở lại.
- Tiền cọc của host không thay đổi.
- Nếu số người giảm xuống dưới `minPlayers`, lobby quay lại trạng thái tuyển người.
- Nếu đến deadline vẫn dưới `minPlayers`, áp dụng BR-REFUND-01 (hoàn 100% cho host).
- Nếu thành viên thường xuyên rời lobby hoặc rời sát giờ, trừ Karma nhưng không xử lý bằng tiền.

### 4.12. Quán hủy booking

**BR-REFUND-04**:

- Hoàn 100% BVC về ví của host.
- Giải phóng lobby và reservation.
- Không bồi thường BVC thêm.
- Không trừ Karma cho host và thành viên.

### 4.13. Doanh thu của quán

**BR-REVENUE-01**: Theo quyết định của nhóm (admin không thu phí):

- Tiền cọc thuộc **100% về quán** khi booking đủ điều kiện ghi nhận.
- Quán nhận tiền cọc khi player check-in hoặc khi quá hạn cho phép hủy (mục 4.10).
- Admin/platform không thu phần trăm.
- Khoản cọc **chưa** được ghi nhận ngay khi lobby vừa tạo vì vẫn có thể thất bại và hoàn 100%.

### 4.14. Không refund BVC về tiền thật

**BR-REFUND-05**:

- BVC đã nạp vào ví không thể rút ra tiền mặt.
- BVC không thể chuyển cho player khác.
- Nếu nạp BVC nhưng reservation thất bại do hết chỗ:
  - Số tiền vẫn trở thành `availableBalance` của player.
  - Không refund về phương thức thanh toán ban đầu.
  - Player dùng BVC đó cho booking khác hoặc tiếp tục giữ trong ví.

### 4.15. Phân biệt hạn mức theo khoảng cách `playDate`

**BR-NEW-01**: Hạn mức `maxPlayers` và `minDeposit` thay đổi theo khoảng cách giữa `now` và `playDate`:

| Khoảng cách `playDate` | `maxPlayers` tối đa | `minDeposit` (BVC) | Yêu cầu bổ sung |
|------------------------|---------------------|---------------------|-------------------|
| Hôm nay (cùng ngày) | 30 | 50.000 | Buffer ≥ 120 phút |
| 1 ngày sau | 20 | 50.000 | Buffer ≥ 120 phút |
| 2 ngày sau | 15 | 100.000 | Cần cafe duyệt nếu `maxPlayers` > 10 |
| 3–4 ngày sau | 10 | 150.000 | Cần cafe duyệt |
| 5–7 ngày sau | 6 | 200.000 | Cần cafe duyệt |

Lý do:

- Lobby càng xa trong tương lai, rủi ro no-show, đổi ý, spam chiếm chỗ càng cao.
- Hạn chế `maxPlayers` giúp giảm thiệt hại cho cafe nếu lobby fail.
- Tăng `minDeposit` giúp tăng rào cản tài chính cho spam.

**BR-NEW-02 (định nghĩa lại)**: Một user chỉ được là host của tối đa **1 lobby active** cho mỗi `playDate` cụ thể.

Ví dụ:

- Host tạo lobby slot morning ngày 09/08 → không tạo thêm lobby nào khác cho ngày 09/08.
- Host vẫn có thể tạo lobby cho ngày 10/08.
- Sau khi lobby ngày 09/08 terminal, host có thể tạo lobby mới cho ngày 09/08.

**BR-NEW-05 (định nghĩa lại)**: Tối đa **5 lần tạo/hủy lobby** của host cho cùng một `playDate` (chỉ tính hành động chủ động của host, không tính auto-extend slot).

Lý do:

- Tránh spam tạo/hủy liên tục.
- Chống gian lận cọc (tạo hủy tạo hủy để thử nghiệm).

### 4.16. Chống spam cọc chiếm chỗ

**BR-NEW-08**: Trong 1 cafe cụ thể, 1 user chỉ được có tối đa **1 lobby active** với cùng `playDate` + `timeSlot`.

Ví dụ:

- Host tạo lobby ở Cafe A, slot morning ngày 09/08 → OK.
- Host muốn tạo lobby ở Cafe A, slot morning ngày 09/08 khác → **từ chối**.
- Host có thể tạo lobby ở Cafe A, slot afternoon ngày 09/08 → OK.
- Host có thể tạo lobby ở Cafe B, slot morning ngày 09/08 → OK (khác cafe).

Lý do:

- Tránh 1 user chiếm nhiều slot trong cùng 1 quán.
- Cafe kiểm soát tốt hơn lịch sử dụng.

**BR-USER-LIMIT-03 (xem mục 4.8)**: Cap tổng `heldBalance` theo loại user.

### 4.17. Cooling-off và yêu cầu cafe duyệt

**BR-NEW-10 (Cooling-off)**: Nếu user rơi vào một trong các điều kiện sau:

- Lobby timeout (`timeoutFailed`) liên tiếp **3 lần** trong vòng **7 ngày**.
- Lobby bị host hủy sau grace (`hostCancelled`) liên tiếp **3 lần** trong **7 ngày**.
- Tổng cọc bị forfeit/no-show vượt **500.000 BVC** trong **30 ngày**.

→ Kích hoạt **cooling-off**:

- User không được tạo lobby có `playDate` > 1 ngày trong tương lai.
- Cọc nhân **×2** cho mọi lobby mới (kết hợp với `riskMultiplier` hiện tại).
- Thời hạn cooling-off: **30 ngày**, sau đó hệ thống tự đánh giá lại.
- Trong thời gian cooling-off, nếu user tiếp tục fail → gia hạn thêm 30 ngày, cọc ×3.

**BR-NEW-11 (Cafe duyệt lobby public > 2 ngày)**: Lobby **public** có `playDate` cách `now` ≥ 2 ngày phải được cafe duyệt trước khi publish. Lobby **private** (mời bạn) không cần cafe duyệt dù playDate xa.

Quy trình (chỉ áp dụng cho lobby **public**):

1. Host tạo lobby → status = `pendingCafeApproval`.
2. Cafe nhận notification (in-app + POS).
3. Cafe duyệt hoặc từ chối trong **24 giờ**.
4. Nếu duyệt: status = `open`, công khai, lobby bắt đầu tuyển người.
5. Nếu từ chối: status = `rejectedByCafe`, hoàn **100% BVC** cho host.
6. Nếu quá 24 giờ không phản hồi: status tự động chuyển sang `expiredByCafe`, hoàn 100% BVC.

**Ngoại lệ lobby private:**
- Lobby `IsPrivate = true` (mời bạn qua share code/invite) **không cần** cafe duyệt dù playDate xa.
- Status khởi tạo = `open` luôn, không đi qua `pendingCafeApproval`.
- Chỉ invited members mới thấy lobby trong discovery.

Lý do:

- Cafe chủ động kiểm soát lịch, chống spam chiếm chỗ.
- Cafe có quyền từ chối lobby có dấu hiệu spam (nhiều lobby cùng ngày, maxPlayers cao bất thường).
- Lobby gần (cùng ngày, 1 ngày sau) không cần duyệt để tránh cản trở trải nghiệm.

### 4.18. Cấu hình cafe và notification

**BR-NEW-12 (Cafe cấu hình hạn mức riêng)**: Cafe có thể cấu hình linh hoạt các hạn mức:

```yaml
cafe_config:
  capacity: 30
  max_lobbies_per_user_per_day: 1
  max_players_per_lobby_same_day: 30
  max_players_per_lobby_1_day: 20
  max_players_per_lobby_2_days: 15
  max_players_per_lobby_3_to_4_days: 10
  max_players_per_lobby_5_to_7_days: 6
  min_deposit_same_day: 50000
  min_deposit_1_day: 50000
  min_deposit_2_days: 100000
  min_deposit_3_to_4_days: 150000
  min_deposit_5_to_7_days: 200000
  require_approval_for_distant: true
  distant_threshold_days: 2
  approval_timeout_hours: 24
  max_total_deposit_per_user: 500000
  recruitment_deadline_buffer_minutes: 120
  cancellation_grace_minutes: 15
```

Nếu cafe không cấu hình: áp dụng giá trị mặc định ở **BR-NEW-01** và các BR liên quan.

Các giá trị cafe config **không được vượt** giới hạn an toàn toàn hệ thống (ví dụ `max_total_deposit_per_user` không vượt 1.000.000 BVC).

**BR-NEW-13 (Notification nhắc nhở)**: Hệ thống gửi notification theo các mốc thời gian:

| Mốc | Người nhận | Nội dung |
|-----|-----------|----------|
| 48 giờ trước `recruitmentDeadline` | Host | "Lobby XYZ còn 48 giờ để tuyển đủ người. Hiện có X/Y." |
| 24 giờ trước `recruitmentDeadline` | Host + Members | "Lobby XYZ sắp đến deadline. Còn thiếu X người." |
| 2 giờ trước `playDate` (nếu có `preferredStartTime`) | Host + Members | "Lobby XYZ bắt đầu sau 2 giờ tại Cafe Y." |
| 30 phút trước `preferredStartTime` (nếu có) | Host + Members | "Lobby XYZ bắt đầu sau 30 phút. Đừng quên nhé!" |

Nếu lobby không có `preferredStartTime`: bỏ qua các mốc 2 giờ và 30 phút.

**BR-NEW-14 (Cảnh báo lobby có nguy cơ fail)**: Nếu sau **50% thời gian tuyển** (từ lúc tạo đến `recruitmentDeadline`), lobby có `< 50% minPlayers`:

- Gửi notification cho host.
- Kèm các đề xuất:
  - (a) Chia sẻ link mời bạn qua Zalo/Messenger.
  - (b) Đổi sang `timeSlot` khác xa hơn (cùng `playDate` hoặc `playDate` khác).
  - (c) Hủy lobby (hoàn cọc theo BR-REFUND-03).
  - (d) Boost lobby (tính năng tương lai, MVP không cần).

### 4.19. `timeSlot` và công thức tính deadline

**BR-NEW-15 (Định nghĩa `timeSlot`)**: `timeSlot` là một trong các khung giờ cố định trong ngày:

| `timeSlot` | `startTime` | `endTime` | Mô tả |
|------------|-------------|-----------|-------|
| `morning` | 09:00 | 13:00 | Phiên sáng |
| `afternoon` | 13:00 | 18:00 | Phiên chiều |
| `evening` | 18:00 | 23:00 | Phiên tối |
| `lateNight` | 23:00 | 06:00 | Phiên khuya qua đêm (endTime = 06:00 ngày hôm sau) |

**BR-NEW-15a (Công thức tính deadline)**:

```
scheduledTime = playDate + timeSlot.startTime
recruitmentDeadline = scheduledTime - leadTimeMinutes
```

**BR-NEW-15b (`preferredStartTime` tham chiếu)**: Host có thể chọn thêm `preferredStartTime` (optional, kiểu `TimeOfDay`) để member biết giờ dự kiến bắt đầu.

- `preferredStartTime` phải nằm trong khoảng `[timeSlot.startTime, timeSlot.endTime]`.
- Nếu không chọn: member chỉ thấy "Khung giờ chiều", không rõ giờ chính xác.
- `preferredStartTime` **không thay thế** deadline, chỉ là tham chiếu cho member.

**Ví dụ tính deadline:**

```
Host tạo lobby:
  playDate = 09/08/2026 (Chủ nhật)
  timeSlot = afternoon (start 13:00, end 18:00)
  preferredStartTime = 14:00
  leadTimeMinutes = 20 (mặc định)

→ scheduledTime = 09/08/2026 13:00
→ recruitmentDeadline = 09/08/2026 12:40
→ Buffer = recruitmentDeadline - now

Trường hợp 1: now = 02/08/2026 10:00
  Buffer = 7 ngày 2 giờ 40 phút = 10.240 phút (rất dài) ✓

Trường hợp 2: now = 09/08/2026 08:00
  Buffer = 4 giờ 40 phút = 280 phút (≥ 120 phút) ✓

Trường hợp 3: now = 09/08/2026 11:30
  Buffer = 1 giờ 10 phút = 70 phút (60 ≤ buffer < 120) → Cảnh báo UI ✓

Trường hợp 4: now = 09/08/2026 12:00
  Buffer = 40 phút (< 60) → Từ chối tạo ✗
```

**Lợi ích của `timeSlot`:**

- Member nhìn lobby biết ngay "phiên sáng/chiều/tối", không cần đoán.
- Host không bị ràng buộc giờ chính xác (vì thực tế không thể đúng giờ).
- Cafe dễ phân bổ bàn, nhân viên theo khung giờ lớn.
- Đơn giản hơn so với `scheduledTime` chính xác đến phút.

---

## 5. Trạng thái domain

### 5.1. Trạng thái Reservation / Booking

```
draft
   ↓ (host submit config)
awaitingDeposit
   ↓ (host có đủ BVC hoặc vừa top-up)
holding              ← tài nguyên đang được giữ, lobby đang tuyển người
   ↓ (đạt minPlayers trước deadline)         ↓ (deadline trôi qua, không đủ người)
confirmed             expired
   ↓ (player check-in tại quán)
checkedIn
   ↓ (POS đóng phiên chơi)
completed

Bất kỳ lúc nào có thể chuyển sang:
cancelledByPlayer
cancelledByCafe
noShow
```

Chi tiết:

- **draft**: cấu hình chưa hoàn tất.
- **awaitingDeposit**: chờ khóa BVC hoặc top-up.
- **holding**: đã khóa BVC, đã giữ ghế và game copy, lobby đang hoạt động.
- **confirmed**: lobby đạt điều kiện tối thiểu, booking được xác nhận.
- **checkedIn**: quán đã quét QR check-in.
- **completed**: phiên chơi kết thúc.
- **expired**: không đủ người trước deadline.
- **cancelledByPlayer**: host hủy (áp dụng chính sách 4.10).
- **cancelledByCafe**: quán hủy (BR-REFUND-04).
- **noShow**: host không đến.

### 5.2. Trạng thái Deposit (Ledger entry type kèm status)

```
pending          ← đang chờ top-up hoặc chờ xác nhận
held             ← BVC đã bị giữ cho reservation
released         ← BVC đã trả về available (do hủy, timeout)
captured         ← BVC đã chuyển cho quán (khi check-in hoặc quá hạn)
forfeited        ← BVC đã mất do no-show hoặc hủy sát giờ
failed           ← lỗi kỹ thuật
```

### 5.3. Trạng thái Lobby

```
pendingActivation  ← giao dịch atomic đang xử lý, lobby chưa publish
open               ← đang tuyển người, recruitmentDeadline chưa tới
viable             ← đạt minPlayers, vẫn có thể nhận thêm
full               ← đạt maxPlayers, ngừng nhận
inProgress         ← đã check-in tại quán
closed             ← phiên chơi kết thúc, có thể mở cửa sổ đánh giá Karma
timeoutFailed      ← deadline trôi qua, không đủ người
hostCancelled      ← host hủy chủ động
```

### 5.4. Trạng thái Seat/Game Reservation

```
available    ← chưa được ai giữ
held         ← lobby/reservation đang giữ
released     ← vừa được giải phóng, đang trong cooldown
inUse        ← player đã check-in, đang sử dụng
```

---

## 6. Luồng nghiệp vụ chi tiết

### 6.1. Top-up BVC

```
Player mở màn hình Wallet
  → Nhập số tiền (hoặc chọn gói)
  → Backend tính BVC nhận được (amount / 1.000)
  → Mở Payment Gateway (VNPay/MoMo mock)
  → Gateway trả kết quả success
  → Backend ghi ledger: TOP_UP, availableBalance += X BVC
  → Idempotency key được lưu để tránh trùng
```

**Validation**:

- Số tiền ≥ 10.000 VND.
- Số tiền chia hết cho 1.000.
- Không cho phép hai yêu cầu top-up trùng `idempotencyKey`.

### 6.2. Tạo lobby (Flow chính)

```
Player vào màn hình cấu hình lobby
  → Chọn game, cafe, ngày giờ, maxPlayers, minPlayers, public/private,
    karma tối thiểu, bán kính
  → Nhấn "Tạo phòng"
  → Backend kiểm tra:
      - User có quyền tạo lobby (chưa vượt giới hạn active)
      - Lịch không chồng lấn với booking khác
      - Cafe có cấu hình cọc hợp lệ
  → Trả về quote:
      {
        cafeId, gameId, scheduledTime,
        minPlayers, maxPlayers,
        depositRatePerPerson, baseDeposit,
        riskMultiplier, finalDeposit,
        currentBalance, missingAmount,
        expiresAt  // quote hết hạn sau 2-5 phút
      }
```

### 6.3. Xác nhận đặt cọc (Atomic transaction)

```
Player xem quote → nhấn "Xác nhận"
  → Nếu currentBalance < finalDeposit:
        Mở màn hình top-up → Player nạp thêm
        → Quay lại bước này với quote còn hạn
  → Nếu đủ:
        Backend thực hiện atomic:
          1. Validate lại quote (chưa hết hạn)
          2. Kiểm tra available seats >= maxPlayers
          3. Kiểm tra game copy còn khả dụng
          4. Kiểm tra user vẫn đủ điều kiện
          5. Trừ availableBalance, cộng heldBalance
          6. Tạo Reservation(status = holding)
          7. Tạo Lobby(status = pendingActivation)
          8. Bind reservation ↔ lobby
          9. Commit transaction
          10. Publish lobby → chuyển sang open
          11. Phát SignalR event "LobbyActivated"
          12. Push notification tới player phù hợp
        → Trả về lobbyId
        → App navigate tới LobbyPage
```

Nếu bất kỳ bước nào thất bại: rollback toàn bộ. Không có tình trạng trừ BVC nhưng không tạo được lobby.

### 6.4. Tuyển người vào lobby

```
Member mở màn hình Discovery / Lobby lân cận
  → Thấy lobby open
  → Nhấn "Tham gia"
  → Backend validate:
      - Member không bị lịch chồng lấn
      - Karma >= minimumKarma của lobby
      - Lobby chưa full
  → Member join lobby
  → SignalR broadcast MemberJoinedEvent
  → Lobby cập nhật currentPlayers
  → Nếu currentPlayers == maxPlayers:
        Lobby chuyển full
        Tự động đóng tuyển
        Booking chuyển confirmed
  → Nếu minPlayers <= currentPlayers < maxPlayers:
        Lobby chuyển viable
        Booking vẫn holding nhưng có thể chuyển confirmed
        nếu đạt maxPlayers hoặc đến deadline
```

### 6.5. Đến deadline

```
Backend cron job hoặc scheduler quét lobby đến deadline
  → Nếu currentPlayers >= minPlayers:
        Lobby chuyển viable (hoặc full nếu đạt max)
        Booking chuyển confirmed
        SignalR phát LobbyConfirmedEvent
        Host nhận notification
  → Nếu currentPlayers < minPlayers:
        Lobby chuyển timeoutFailed
        Booking chuyển expired
        Giải phóng seat và game copy
        Hoàn 100% BVC về availableBalance của host
        Ghi ledger: DEPOSIT_RELEASE
        SignalR phát LobbyTimeoutEvent
```

### 6.6. Hủy lobby bởi host

```
Host nhấn "Hủy lobby"
  → Backend kiểm tra thời điểm hủy (BR-REFUND-02)
  → Tính số BVC được hoàn
  → Cập nhật Lobby.status = hostCancelled
  → Cập nhật Booking.status = cancelledByPlayer
  → Giải phóng seat và game copy
  → Ghi ledger tương ứng (DEPOSIT_RELEASE hoặc DEPOSIT_FORFEIT)
  → Điều chỉnh Karma theo BR-REFUND-02
  → SignalR broadcast LobbyCancelledEvent
```

### 6.7. Check-in tại quán

```
Host đến quán, mở BookingSuccessPage, hiển thị QR
  → Staff quét QR trên POS
  → POS validate:
      - Booking tồn tại, status = confirmed
      - Nonce chưa được sử dụng
      - Thời gian nằm trong khung giờ cho phép
  → Cập nhật Booking.status = checkedIn
  → Nonce.used = true
  → Trừ game copy đã giữ → chuyển sang inUse (gán barcode cụ thể)
  → Bắt đầu billing session trên POS
```

### 6.8. Hoàn thành phiên chơi

```
POS đóng session sau khi player trả tiền tại quán
  → Cập nhật Booking.status = completed
  → BVC đã giữ (heldBalance) được capture:
        Ghi ledger DEPOSIT_CAPTURE
        Settlement += finalDeposit (doanh thu quán)
  → Cập nhật Karma cho các thành viên
  → Mở cửa sổ đánh giá Karma
```

### 6.9. No-show

```
Sau scheduledTime + gracePeriodMinutes (mặc định 30 phút)
  → Nếu Booking.status vẫn confirmed mà chưa checkedIn:
        Cập nhật Booking.status = noShow
        Lobby.status = closed (nếu chưa đóng)
        Ghi ledger DEPOSIT_FORFEIT:
            heldBalance -= finalDeposit
            forfeit += finalDeposit (doanh thu quán)
        Giảm Karma đáng kể cho host
        Có thể tăng riskMultiplier cho lần sau
```

### 6.10. App resume flow

```
App khởi động / quay lại từ background
  → Đọc pendingBookingId từ SecureStorage
  → Gọi API get booking by id
  → Nếu status = holding/confirmed:
        Navigate tới LobbyPage
  → Nếu status = checkedIn:
        Navigate tới BookingSuccessPage
  → Nếu status là terminal (completed/expired/cancelled/noShow):
        Clear storage
```

### 6.11. Private lobby & mời bạn

**BR-LOBBY-PRIVACY-01**: Host chọn `isPrivate = true` khi tạo lobby:

- Lobby **không xuất hiện** trong `GET /api/v1/lobbies/search`.
- Chỉ thành viên hiện tại của lobby hoặc người có invite/share code mới truy cập được chi tiết lobby.
- Host có thể đổi `isPrivate` từ `true → false` để mở lobby công khai (chỉ khi lobby vẫn ở trạng thái `open` và chưa đạt `minPlayers`).
- **Không cho phép** đổi từ `false → true` sau khi lobby đã có thành viên tham gia (tránh abuse: mở public → có người join → chuyển private để khóa member cũ).

**BR-LOBBY-PRIVACY-02 (Share code)**:

- Mỗi lobby khi tạo được hệ thống sinh tự động `ShareCode` (6 ký tự alphanumeric uppercase, ví dụ `A3K9P2`).
- `ShareCode` **unique trong toàn hệ thống** (DB unique index).
- Chỉ **thành viên active** của lobby mới xem được `ShareCode` qua `GET /api/v1/lobbies/{lobbyId}/share-info` (tránh user ngoài spam thử code).
- Share code dùng để chia sẻ nhanh qua Zalo/Messenger — người nhận click vào link → mở app → gọi `POST /api/v1/lobbies/join-by-code`.

**BR-LOBBY-PRIVACY-03 (Join bằng share code)**:

- Public lobby: bất kỳ user nào có code đều join được (vẫn phải pass các BR khác: BR-USER-LIMIT-01/02/04, karma, overlap).
- Private lobby: **chỉ user là bạn bè (Friendship.Status = Accepted)** với ít nhất 1 thành viên active của lobby mới join được bằng share code.
- Lý do: share code có thể bị leak ra ngoài, nhưng join vẫn phải qua friendship gate → tránh lộ lobby riêng cho người lạ.

**BR-LOBBY-INVITE-01 (Uniqueness)**: Với mỗi cặp `(LobbyId, InviteeId)` chỉ có **tối đa 1 record `LobbyInvite` ở trạng thái `Pending`** tại một thời điểm.

- Nếu đã có pending invite → trả lỗi 409 "Đã có lời mời đang chờ".
- Nếu invite cũ đã `Declined`/`Expired`/`Cancelled` → cho phép gửi invite mới.

**BR-LOBBY-INVITE-02 (Inviter phải là thành viên active)**: Người gửi invite phải là thành viên active của lobby (host hoặc member chưa rời). Host mới tạo lobby luôn pass.

**BR-LOBBY-INVITE-03 (Invitee chưa là thành viên)**: Người nhận invite **không được** đang là thành viên active của lobby đó. Nếu đã là member → trả lỗi 409 "Đã là thành viên".

**BR-LOBBY-INVITE-04 (Block check)**: Nếu giữa `inviter` và `invitee` tồn tại `Friendship.Status = Blocked` (bất kỳ chiều nào) → **cấm gửi invite**, trả lỗi 403.

**BR-LOBBY-INVITE-05 (Private lobby chỉ mời bạn bè)**:

- Với `lobby.isPrivate = true`: người gửi invite **BẮT BUỘC** phải có quan hệ bạn bè `Accepted` với invitee. Nếu không phải bạn bè → trả lỗi 403.
- Với `lobby.isPrivate = false` (public): vẫn cần check BR-LOBBY-INVITE-04 (không bị block) nhưng không bắt buộc phải là bạn bè.

**BR-LOBBY-INVITE-06 (Accept tự động join)**:

- Khi invitee accept invite, hệ thống **tự động gọi `JoinLobbyAsync`** với đầy đủ validation (BR-USER-LIMIT-01/02/04, karma, overlap, full).
- Nếu validation fail → invite giữ nguyên `Pending` (KHÔNG tự động `Declined`/`Expired`) để user biết lý do và retry sau.
- Nếu lobby đã đầy hoặc đóng → set invite `Expired`, trả 409.

**BR-LOBBY-INVITE-07 (Friend re-check khi accept private lobby)**:

- Khi accept invite của **private lobby**, hệ thống **re-check** quan hệ bạn bè giữa inviter và invitee ngay tại thời điểm accept.
- Nếu 2 bên đã unfriend trước khi accept → set invite `Cancelled`, trả 403.
- Lý do: tránh trường hợp user gửi invite khi còn là bạn, sau đó unfriend trước khi accept → vẫn join được lobby riêng.

**BR-LOBBY-INVITE-08 (Expiry 24h)**:

- Mỗi invite có `ExpiresAt = CreatedAt + 24 giờ`.
- Cron job định kỳ quét và set status `Expired` cho các invite quá hạn mà vẫn `Pending`.
- Khi lobby đóng (host cancel, timeout, full → check-in) → tất cả pending invite của lobby đó chuyển sang `Expired`.

**BR-LOBBY-INVITE-09 (Hết hạn do lobby đóng)**: Khi lobby chuyển sang terminal state (`timeoutFailed`, `hostCancelled`, `cancelledByCafe`, `inProgress`):

- Tất cả `LobbyInvite` còn `Pending` của lobby đó → set `Expired` + `RespondedAt = now`.
- Không cần chờ cron job 24h.

**BR-LOBBY-INVITE-10 (Limit spam)**:

- 1 user không được nhận quá **20 invite `Pending`** trong cùng 1 ngày (chống spam invite).
- 1 user không được gửi quá **30 invite** trong cùng 1 ngày (chống abuse ngược).
- Vượt limit → trả lỗi 429 hoặc 409 (tùy endpoint).

**API endpoints (lobby invite):**

```
POST   /api/v1/lobbies/{lobbyId}/invites            # gửi invite
POST   /api/v1/lobbies/invites/{inviteId}/accept    # accept + auto-join
POST   /api/v1/lobbies/invites/{inviteId}/decline   # decline
DELETE /api/v1/lobbies/invites/{inviteId}           # inviter cancel
GET    /api/v1/lobbies/invites/me/pending           # inbox pending
GET    /api/v1/lobbies/invites/me                   # all (filter status)
GET    /api/v1/lobbies/{lobbyId}/share-info         # lấy ShareCode
POST   /api/v1/lobbies/join-by-code                 # join bằng ShareCode
```

**Luồng ví dụ (private lobby):**

```
Host A tạo lobby Catan tối 09/08 với IsPrivate = true.
  → Hệ thống sinh ShareCode = "A3K9P2".

Host A mời User B (là bạn bè):
  → POST /api/v1/lobbies/{lobbyA}/invites { inviteeId: B }
  → LobbyInvite(status=Pending, expiresAt=now+24h)

User B nhận notification → mở app:
  → GET /api/v1/lobbies/invites/me/pending
  → Thấy invite từ Host A → Accept
  → POST /api/v1/lobbies/invites/{id}/accept
    → Re-check friendship A↔B = Accepted ✓
    → JoinLobbyAsync(B) → BR-USER-LIMIT-01/02/04 pass → B thành member
    → LobbyInvite.status = Accepted

Hoặc share qua Zalo:
  Host A copy link: https://app.boardverse.vn/join/A3K9P2
  → User C click link → mở app:
    → POST /api/v1/lobbies/join-by-code { shareCode: "A3K9P2" }
    → Backend check: lobby A là private → C phải là bạn bè của ít nhất 1 member.
    → Nếu C không phải bạn ai → 403 "Lobby riêng tư yêu cầu quan hệ bạn bè".
    → Nếu C là bạn của A → JoinLobbyAsync(C) → OK.
```

---

## 7. Yêu cầu kỹ thuật bắt buộc

### 7.1. Idempotency

Mọi request quan trọng đều phải có `idempotencyKey`:

- Top-up BVC.
- Xác nhận đặt cọc (tạo reservation/lobby).
- Cancel lobby.
- Refund.

Cùng key + cùng payload → trả về kết quả cũ, không xử lý lại.

### 7.2. Server authoritative

- Client chỉ hiển thị số dư, quote, trạng thái.
- Mọi phép tính cọc, kiểm tra availability, refund phải do backend xác nhận.
- Không tin `depositAmount` gửi từ app.
- Không tin `currentBalance` từ app để cấp quyền thanh toán.

### 7.3. Concurrency control

Availability phải dùng một trong các cơ chế:

- Database transaction (Serializable Isolation).
- Optimistic concurrency với version column.
- Distributed lock theo cafe + khung giờ.

Chỉ `SELECT available > 0` rồi `INSERT` là chưa đủ. Race condition vẫn xảy ra.

### 7.4. Atomic create reservation + lobby

Phải là một transaction duy nhất:

```
BEGIN TRANSACTION
  Validate quote
  Validate user eligibility
  Validate seat availability
  Validate game copy availability
  UPDATE wallet: availableBalance -= X; heldBalance += X
  INSERT ledger: DEPOSIT_HOLD
  INSERT reservation(status = holding)
  INSERT lobby(status = pendingActivation)
  UPDATE seat_inventory: hold seats
  UPDATE game_inventory: hold 1 copy
COMMIT
```

Nếu commit thành công mới publish lobby lên SignalR và push notification. Nếu fail, rollback toàn bộ.

### 7.5. Transactional outbox

Sau commit, các sự kiện phải được ghi vào outbox table:

- `LobbyActivated`.
- `ReservationHeld`.
- `DepositHeld`.
- `LobbyConfirmed`.
- `LobbyTimeout`.

Một worker riêng đọc outbox và phát lên SignalR/push. Đảm bảo không có tình trạng DB đã commit nhưng client không nhận được sự kiện.

### 7.6. Audit log

Tất cả các hành động sau đều phải ghi audit:

- Tạo lobby.
- Hủy lobby.
- Refund.
- No-show.
- Thay đổi status reservation.
- Thay đổi Karma.
- Top-up BVC.

Audit log không thể sửa, chỉ append.

---

## 8. Tích hợp giữa các module

### 8.1. Mối quan hệ mới

```
matchmaking_discovery
        ↓
    reservation/deposit confirmation
        ↓
    wallet (BVC)
        ↓
    booking_payment (giữ chỗ)
        ↓
    lobby_management (tuyển người)
        ↓
    booking_payment (xác nhận / check-in)
```

Trước đây:

```
matchmaking_discovery → lobby_management → booking_payment
```

Sự thay đổi:

- `MatchmakingCubit.createLobby()` không gọi trực tiếp `LobbyRepository.createLobby()`.
- Thay vào đó gọi `BookingPaymentRepository.requestReservationQuote()`.
- `LobbyCubit.createLobby()` không còn là entry point độc lập.
- Lobby chỉ được tạo sau khi reservation thành công.
- `_triggerAutoBooking()` khi lobby full không còn cần thiết; booking đã tồn tại từ trước.
- `BookingStatus.pendingDeposit` xử lý trước khi lobby xuất hiện.
- `LobbyEntity.bookingId` trở thành field bắt buộc.
- `BookingSummaryPage` được mở trước `LobbyPage`, không phải sau khi lobby đầy.
- `LobbyAutoBookingCreated` có thể thay bằng `ReservationConfirmed` hoặc `LobbyMinimumReached`.

### 8.2. Wallet module mới

Cần tạo module mới: `lib/features/wallet/`

```
lib/features/wallet/
├── domain/
│   ├── entities/
│   │   ├── wallet_entity.dart           # availableBalance, heldBalance
│   │   ├── topup_quote_entity.dart
│   │   ├── transaction_entity.dart      # ledger entry
│   │   └── reservation_quote_entity.dart
│   └── repositories/
│       └── wallet_repository.dart
├── data/
│   ├── models/
│   │   ├── wallet_model.dart
│   │   ├── topup_quote_model.dart
│   │   ├── transaction_model.dart
│   │   └── reservation_quote_model.dart
│   ├── datasources/
│   │   ├── base/wallet_remote_datasource.dart
│   │   ├── remote/wallet_remote_datasource_impl.dart
│   │   └── mock/wallet_remote_datasource.dart
│   └── wallet_repository_impl.dart
└── presentation/
    ├── cubit/
    │   ├── wallet_cubit.dart
    │   ├── wallet_state.dart
    │   ├── topup_cubit.dart
    │   └── topup_state.dart
    ├── pages/
    │   ├── wallet_page.dart              # Số dư + lịch sử
    │   ├── topup_page.dart
    │   └── transaction_history_page.dart
    └── widgets/
        ├── balance_card.dart
        └── transaction_tile.dart
```

### 8.3. Reservation module mới

Tách phần "đặt cọc + giữ chỗ" ra khỏi `booking_payment` thành module `reservation`:

```
lib/features/reservation/
├── domain/
│   ├── entities/
│   │   ├── reservation_entity.dart
│   │   ├── reservation_quote_entity.dart
│   │   └── deposit_config_entity.dart   # đã có trong booking_payment, có thể move
│   └── repositories/
│       └── reservation_repository.dart
├── data/
│   └── ...
└── presentation/
    ├── cubit/
    │   ├── reservation_quote_cubit.dart
    │   └── reservation_state.dart
    ├── pages/
    │   ├── reservation_quote_page.dart  # bước "Xem chi tiết cọc"
    │   └── reservation_confirmed_page.dart
    └── widgets/
        ├── deposit_breakdown_card.dart
        └── countdown_to_deadline.dart
```

`booking_payment` sau khi tách chỉ còn phần: thanh toán tại quán, QR check-in, lịch sử booking.

---

## 9. Bảng tổng hợp Business Rules

| ID | Quy tắc | Triển khai |
|----|----------|-------------|
| BR-DEPOSIT-01 | Host trả toàn bộ cọc | `Reservation.hostId` và `Deposit.hostId` |
| BR-DEPOSIT-02 | `depositAmount = ratePerPerson × maxPlayers` | `ReservationQuote` |
| BR-DEPOSIT-03 | Giới hạn min/max rate | Validate ở backend |
| BR-DEPOSIT-04 | Áp dụng `riskMultiplier` theo lịch sử | Background job tính risk score |
| BR-RESERVATION-01 | Giữ `maxPlayers` ghế | Atomic reservation |
| BR-RESERVATION-02 | Giữ 1 game copy | Atomic reservation |
| BR-LOBBY-01 | Có `recruitmentDeadline` | `scheduledTime - leadTimeMinutes` |
| BR-LOBBY-01a | Buffer tối thiểu 120 phút | Validate lúc tạo lobby |
| BR-LOBBY-01b | Từ chối nếu buffer < 60 phút | Validate lúc tạo lobby |
| BR-LOBBY-01c | Cảnh báo nếu buffer 60-120 phút | UI warning |
| BR-LOBBY-02 | Đạt `minPlayers` là đủ điều kiện xác nhận | Scheduler check deadline |
| BR-LOBBY-03 | Member rời lobby không ảnh hưởng cọc host | `leaveLobby()` không refund |
| BR-USER-LIMIT-01 | Max 1 lobby host active, max 2 lobby tổng | Validate trước khi tạo/join |
| BR-USER-LIMIT-02 | Không lịch chồng lấn | Validate overlap interval |
| BR-USER-LIMIT-03 | Tổng cọc held ≤ 500k (user thường) | Validate lúc tạo lobby |
| BR-USER-LIMIT-04 | Player join lobby → không được host lobby khác | Validate trước khi tạo |
| BR-USER-LIMIT-05 | **ĐÃ BỎ** — Host được phép join lobby khác nếu không overlap | Validate trước khi join |
| BR-REFUND-01 | Timeout: hoàn 100% | Scheduler |
| BR-REFUND-02 | Hủy theo mốc thời gian | `cancelLobby()` |
| BR-REFUND-03 | Grace period 15 phút | `cancelLobby()` với điều kiện |
| BR-REFUND-04 | Quán hủy: hoàn 100%, không bồi thường | `cancelByCafe()` |
| BR-REFUND-05 | BVC không rút về tiền thật | UI + API validate |
| BR-REVENUE-01 | Cọc 100% về quán, admin không thu phí | Settlement logic |
| BR-NEW-01 | Phân biệt maxPlayers + minDeposit theo khoảng cách playDate | Validate lúc tạo lobby |
| BR-NEW-02 | 1 lobby active / playDate / user | Validate lúc tạo lobby |
| BR-NEW-05 | Tối đa 5 lần tạo/hủy / playDate | Counter theo host + playDate |
| BR-NEW-08 | 1 lobby active / playDate+timeSlot / cafe / user | Validate lúc tạo lobby |
| BR-NEW-10 | Cooling-off nếu fail 3 lần / 7 ngày | Background job theo dõi |
| BR-NEW-11 | Cafe duyệt lobby > 2 ngày | Workflow approval |
| BR-NEW-12 | Cafe cấu hình hạn mức riêng | `cafe_config` table |
| BR-NEW-13 | Notification 4 mốc (48h/24h/2h/30p) | Scheduled job |
| BR-NEW-14 | Cảnh báo lobby có nguy cơ fail | Check 50% thời gian tuyển |
| BR-NEW-15 | Định nghĩa `timeSlot` (morning/afternoon/evening/lateNight) | Enum + lookup table |
| BR-NEW-15a | Công thức tính deadline từ playDate + timeSlot | Backend logic |
| BR-NEW-15b | `preferredStartTime` tham chiếu trong timeSlot | Optional field |
| BR-RISK-01 | Tính `riskScore` (0-100) từ 10 signals | Scheduled job `risk_score_recompute` |
| BR-RISK-02 | Auto-trigger khi cross ngưỡng 30/50/75 | Real-time + audit log |
| BR-RISK-03 | `riskMultiplier = 1.0 + (riskScore / 100) × 1.0` | `WalletEntity.riskMultiplier` |
| BR-RISK-04 | 5 trạng thái tài khoản (active/warning/restricted/suspended/banned) | Validate tại API layer |
| BR-RISK-05 | Mọi admin action ghi audit log vĩnh viễn | `player_action_history` table |
| BR-RISK-06 | Admin action có thời hạn tự động hết hạn | Job `suspension_expiry_check` |
| BR-RISK-07 | 3 admin roles: Support, Risk, Senior | RBAC middleware |
| BR-RISK-08 | Multi-account detection: 2 tín hiệu trùng / 30 ngày | Job `signal_detect_multi_account` |
| BR-RISK-09 | User chỉ thấy `riskLevel`, không thấy `riskScore` chi tiết | UI hide |
| BR-RISK-10 | User bị `suspended` được khiếu nại trong 48 giờ | Support ticket workflow |
| BR-RISK-11 | Lưu `riskScore` history 365 ngày (partition theo tháng) | `risk_score_history` table |
| **BR-LOBBY-PRIVACY-01** | Host chọn `IsPrivate` khi tạo lobby; private lobby không xuất hiện trong search | `Lobby.IsPrivate`, search filter |
| **BR-LOBBY-PRIVACY-02** | `ShareCode` sinh unique 6 ký tự alphanumeric uppercase; chỉ member active xem được | `Lobby.ShareCode`, `GET /share-info` |
| **BR-LOBBY-PRIVACY-03** | Private lobby chỉ join bằng share code nếu user là bạn bè của ít nhất 1 member | `JoinLobbyByShareCodeAsync` |
| **BR-LOBBY-INVITE-01** | Mỗi `(LobbyId, InviteeId)` chỉ có 1 `Pending` invite tại 1 thời điểm | `LobbyInviteRepository.GetPendingInviteAsync` |
| **BR-LOBBY-INVITE-02** | Inviter phải là thành viên active của lobby | `LobbyInviteService.SendInviteAsync` |
| **BR-LOBBY-INVITE-03** | Invitee không được đang là thành viên active | `LobbyInviteService.SendInviteAsync` |
| **BR-LOBBY-INVITE-04** | Không gửi invite nếu 2 bên có `Friendship.Status = Blocked` | `IFriendshipRepository.GetByPairAsync` |
| **BR-LOBBY-INVITE-05** | Private lobby: inviter BẮT BUỘC là bạn bè `Accepted` của invitee | `LobbyInviteService.SendInviteAsync` |
| **BR-LOBBY-INVITE-06** | Accept invite tự động `JoinLobbyAsync`; nếu fail giữ `Pending` (không auto-decline) | `AcceptInviteAsync` |
| **BR-LOBBY-INVITE-07** | Private lobby: re-check friendship tại thời điểm accept | `AcceptInviteAsync` |
| **BR-LOBBY-INVITE-08** | Invite hết hạn sau 24h (cron job đánh `Expired`) | Background job |
| **BR-LOBBY-INVITE-09** | Lobby terminal → tất cả pending invite chuyển `Expired` ngay | Lobby close handler |
| **BR-LOBBY-INVITE-10** | Limit 20 pending invite / user / ngày, 30 invite gửi / user / ngày | Counter + validate |

---

## 10. Edge cases cần xử lý

### 10.1. Race condition khi nhiều nhóm cùng đặt

```
Quán còn 6 ghế.
Nhóm A tạo lobby 4 người.
Nhóm B tạo lobby 4 người cùng lúc.
```

- Backend dùng `SELECT FOR UPDATE` trên `seat_inventory` của cafe trong khung giờ đó.
- Transaction nào commit trước giữ được 4 ghế, transaction còn lại thất bại.
- Player B nhận thông báo "Quán không đủ chỗ", BVC được trả lại ví.

### 10.2. Game copy bận

```
Quán còn 2 hộp Catan, 6 ghế.
Nhóm A đặt 1 hộp Catan 6 người.
Nhóm B đặt 1 hộp Catan 6 người.
```

- Mỗi transaction phải lock cả `seat_inventory` và `game_inventory`.
- Một nhóm giữ được, nhóm còn lại thất bại.

### 10.3. Player nạp tiền nhưng reservation thất bại

```
Player nạp 100 BVC.
Quote 120 BVC → top-up thêm 20 BVC.
Giữ chỗ thất bại do hết chỗ.
```

- Rollback toàn bộ.
- `availableBalance` vẫn là 100 BVC.
- Player có thể dùng cho booking khác hoặc giữ trong ví.

### 10.4. Host double-tap "Xác nhận"

- Client gửi cùng `idempotencyKey`.
- Backend trả về cùng kết quả, không tạo reservation thứ hai.

### 10.5. Mạng chậm giữa commit DB và phát SignalR

- DB đã commit nhưng SignalR fail.
- Outbox table vẫn ghi event.
- Worker retry cho đến khi thành công.

### 10.6. Host hủy trước khi thành viên nào join

- Áp dụng BR-REFUND-03 (grace 15 phút).
- Hoàn 100% BVC.

### 10.7. Host hủy khi đã có người join

- Hủy sau grace period.
- Áp dụng mức hoàn theo BR-REFUND-02.
- Thành viên nhận notification "Lobby bị hủy".

### 10.8. Thành viên join rồi rời đi liên tục

- Không xử lý bằng tiền.
- Đếm số lần rời lobby trong 30 ngày.
- Nếu > N lần: trừ Karma, có thể hạn chế join lobby có Karma cao.

### 10.9. Cafe thay đổi cấu hình cọc sau khi lobby đã tạo

- Lobby đã tạo giữ cọc theo cấu hình cũ.
- Cafe thay đổi chỉ áp dụng cho lobby mới.
- Lưu `depositConfigSnapshot` trong Reservation để audit.

### 10.10. Multiple devices của cùng một user

- Khi user mở app trên thiết bị thứ hai, `restoreActiveLobby()` vẫn trỏ về đúng lobby.
- Nếu thiết bị thứ hai cố tạo lobby mới: từ chối vì vượt giới hạn active.

### 10.11. Cross-role edge cases (BR-USER-LIMIT-04, 05)

#### 10.11.1. Player join lobby rồi muốn tạo lobby khác làm host

```
Tình huống:
  User A đã join lobby XYZ làm member.
  Lobby XYZ đang tuyển người, status = open.
  User A nhấn "Tạo lobby mới" làm host.

Xử lý:
  Validate BR-USER-LIMIT-04:
    - User A đang là member của lobby ACTIVE.
    - Từ chối tạo lobby mới.
  UI: "Bạn đang tham gia lobby XYZ. Vui lòng rời lobby hoặc đợi lobby kết thúc trước khi tạo lobby mới."

Ngoại lệ:
  Nếu user A rời lobby (currentPlayers còn ≥ minPlayers), cho phép tạo lobby mới.
  Nếu user A rời khiến lobby xuống dưới minPlayers, lobby vẫn tiếp tục tuyển, user A vẫn bị BR-USER-LIMIT-04 cho đến khi lobby terminal.
```

#### 10.11.2. Host tạo lobby rồi muốn join lobby khác làm member

```
Tình huống:
  User B đã tạo lobby ABC làm host.
  Lobby ABC đang tuyển người, status = open.
  User B muốn join lobby DEF (do host khác tạo) làm member.

Xử lý (BR-USER-LIMIT-05 ĐÃ BỎ):
  Validate BR-USER-LIMIT-01: tổng host + member ≤ 2.
  - Nếu User B chưa là member lobby nào (tổng = 1) → CHO PHÉP join.
  - Nếu User B đã là member 1 lobby khác (tổng = 2) → TỪ CHỐI.

  Validate BR-USER-LIMIT-02: không overlap lịch.
  - Nếu lobby DEF không overlap với lobby ABC → CHO PHÉP join.
  - Nếu lobby DEF overlap với lobby ABC (+30 phút buffer) → TỪ CHỐI.

UI: "Phòng bạn muốn tham gia bị trùng lịch với phòng bạn đang host."
```

**Ví dụ hợp lệ:**

- User B host lobby 19:00 thứ 7 (3 ngày sau) → Join lobby 14:00 thứ 7 (khác khung giờ) ✓
- User B host lobby sáng mai → Join lobby tối mai (khác khung giờ) ✓

**Ví dụ bị từ chối:**

- User B host lobby 19:00 hôm nay → Join lobby 19:00 hôm nay (overlap) ✗
- User B host lobby 19:00 + đã là member 1 lobby khác → Tổng = 2 → TỪ CHỐI ✗

#### 10.11.3. Player join 1 lobby, host 1 lobby → tổng 2 lobby

```
Tình huống hợp lệ:
  User C đã host lobby 1, lobby 1 đã terminal (timeoutFailed).
  Sau khi lobby 1 terminal, User C join lobby 2 của host khác.
  Tại thời điểm này: User C chỉ là member của 1 lobby → đúng BR-USER-LIMIT-01 (≤ 2 tổng).

Tình huống bị từ chối:
  User C đã host lobby 1 (đang active).
  User C muốn join lobby 2 làm member, nhưng đã là member 1 lobby khác.
  BR-USER-LIMIT-01 chặn (tổng = 2) → từ chối.
```

#### 10.11.4. Host lobby A, member lobby B, muốn host lobby C

```
Tình huống:
  User D đang là host của lobby A.
  User D đang là member của lobby B.
  User D muốn tạo lobby C làm host mới.

Xử lý:
  BR-USER-LIMIT-01: tổng 2 lobby (A + B), đã đạt max → không thể có lobby thứ 3.
  → Từ chối hoàn toàn.

  User D phải đợi ít nhất 1 trong 2 lobby (A hoặc B) terminal.
```

### 10.12. Spam cọc chiếm chỗ

```
Tình huống:
  Attacker tạo user mới (chưa xác minh).
  Spam tạo lobby ở 28 slot khác nhau (7 ngày × 4 slot), maxPlayers = 30, cọc 50k.
  Tổng cọc: 28 × 50k = 1.400.000 BVC.

Xử lý đa lớp:
  1. BR-USER-LIMIT-03 (cap tổng cọc):
     - User chưa xác minh: cap 100k (nếu áp dụng BR-USER-LIMIT-03 cho user chưa xác minh).
     - Hoặc cap 500k nếu user thường.
     - Sau 2-3 lobby spam, đạt cap → không thể tạo thêm.

  2. BR-NEW-08 (1 lobby / playDate+timeSlot / cafe / user):
     - Sau lobby đầu tiên ở Cafe A, slot morning, ngày X.
     - Lobby thứ 2 cùng Cafe A, cùng slot, cùng ngày → từ chối.

  3. BR-NEW-01 (hạn mức theo khoảng cách):
     - Lobby > 2 ngày: maxPlayers = 6-15 (giảm từ 30 xuống).
     - Lobby > 4 ngày: maxPlayers = 6-10.
     - Không thể spam 30 người cho slot xa.

  4. BR-NEW-11 (cafe duyệt > 2 ngày):
     - Lobby > 2 ngày phải cafe duyệt.
     - Cafe phát hiện pattern spam → từ chối.

  5. BR-NEW-10 (cooling-off):
     - Sau 3 lobby fail/no-show liên tiếp → cooling-off 30 ngày.
     - Cọc ×2, chỉ được đặt trong ngày.

Kết luận:
  Thay vì spam 28 lobby, attacker chỉ spam được 2-3 lobby trước khi bị chặn đa lớp.
```

### 10.13. Cafe thay đổi cấu hình sau khi lobby đã tạo

```
Tình huống:
  Host tạo lobby 09/08, cafe cấu hình maxPlayers = 30.
  Cafe đổi cấu hình maxPlayers = 15 vào ngày 10/08.

Xử lý:
  Lobby đã tạo giữ nguyên cấu hình cũ (snapshot).
  Thay đổi chỉ áp dụng cho lobby mới.
  Reservation.depositConfigSnapshot lưu cấu hình tại thời điểm tạo.
```

### 10.14. Lobby quá giờ recruitmentDeadline nhưng vẫn đang tuyển

```
Tình huống:
  Cron job bị delay 1 phút.
  recruitmentDeadline đã qua, lobby chưa được scheduler xử lý.
  Member thứ 3 join lobby.

Xử lý:
  Backend phải có double-check tại API level:
    - Khi member join: validate recruitmentDeadline > now.
    - Nếu đã qua: từ chối join, trả về "Lobby đã hết hạn tuyển người".
  Scheduler chỉ là biện chính, API vẫn là biện phòng thủ cuối cùng.
```

### 10.15. Cafe không duyệt lobby trong 24 giờ

```
Tình huống:
  Lobby playDate = 10/08, tạo ngày 05/08 (cách 5 ngày, > 2 ngày).
  Lobby chờ cafe duyệt, status = pendingCafeApproval.
  Sau 24 giờ cafe không phản hồi.

Xử lý:
  Background job quét lobby pending > 24 giờ.
  Tự động chuyển status = expiredByCafe.
  Hoàn 100% BVC cho host.
  Gửi notification cho host: "Cafe không duyệt lobby trong 24 giờ, đã hoàn cọc."
```

### 10.16. Multi-account phát hiện tự động

```
Tình huống:
  User A đăng ký từ IP 1.2.3.4 vào ngày 01/08.
  User B đăng ký cùng IP 1.2.3.4 vào ngày 03/08.
  Cả 2 dùng cùng Android ID.
  → 2 tín hiệu trùng: IP + device ID.

Xử lý (BR-RISK-08):
  Scheduled job `signal_detect_multi_account` chạy mỗi 6 giờ.
  Phát hiện 2 tín hiệu trùng → tạo `player_account_link` (status=suspected).
  Tạo `player_alert` (severity=critical) cho admin.
  Risk score của cả 2 user tăng 30 điểm (SIG-07).
  Admin review tại `multi_account_investigation_page`.

Nếu admin xác nhận (status=confirmed):
  Hợp nhất riskScore.
  Khóa account phụ (suspended).
  Hoàn BVC held về account chính (admin chọn).
  Ghi audit log vĩnh viễn.

Nếu admin dismiss (false positive):
  Reset riskScore contribution từ SIG-07.
  Không khóa account.
```

### 10.17. Admin reset risk score khi false positive

```
Tình huống:
  Job `risk_score_recompute` chạy, user X tăng từ 45 lên 78 (critical).
  Admin review signals:
    - SIG-08: 12 lần tạo+hủy lobby cùng playDate.
  Admin xác minh: user đang test app cho gia đình, không phải spam.
  Admin nhấn "Reset risk score" (ADM-05).

Xử lý:
  riskScore = 0, riskLevel = low.
  Status = active.
  Ghi `player_action_history` (actionType=reset_score).
  Ghi audit log lý do: "False positive, user đang test app".

Lưu ý:
  Reset là 1 lần. Nếu signal tiếp tục trigger → riskScore tăng lại.
  Admin không nên reset liên tục. Nếu > 3 lần reset, escalate cho Senior.
```

### 10.18. User bị suspended khiếu nại thành công

```
Tình huống:
  Admin SUSPENDED user Y 7 ngày (ADM-02) vì SIG-04 spam.
  User Y mở app → thấy màn hình "Tài khoản bị tạm khóa".
  Nhấn "Khiếu nại" → tạo ticket:
    - Lý do: "Tôi đang dọn dẹp lobby test cũ, không phải spam."
    - Bằng chứng: screenshot lobby test, lịch sử tạo lobby.

Admin Support review trong 48 giờ:
  - Kiểm tra signals chi tiết.
  - Xác nhận đúng là test → Upheld.
  - Mở khóa ngay lập tức.
  - Reset riskScore về 0.
  - Ghi audit log outcome=upheld.

Nếu admin Reject:
  - Giữ nguyên tình trạng suspended.
  - User nhận notification lý do từ chối.
  - Vẫn có thể khiếu nại tiếp, nhưng Senior Admin phải duyệt.
```

### 10.19. Risk score vượt 75 nhưng admin chưa review

```
Tình huống:
  Job `risk_score_recompute` phát hiện user Z riskScore = 78 (critical).
  Auto-trigger: status = restricted + tạo admin alert.

User Z vào app:
  - Vẫn login được.
  - Có thể JOIN lobby (nếu được mời).
  - KHÔNG thể tạo lobby mới.
  - Top-up vẫn dùng được.

Admin team nhận alert, phải review trong 24 giờ:
  - Nếu thấy nghiêm trọng → ADM-02 (suspend 7 ngày) hoặc ADM-03 (30 ngày).
  - Nếu thấy false positive → ADM-05 (reset score) → revert status = active.
  - Nếu không review sau 24 giờ → vẫn giữ status = restricted cho đến khi admin review.

Lưu ý:
  Status = restricted KHÔNG tự hết hạn. Phải có admin action.
  Đây là trade-off: an toàn cho hệ thống > trải nghiệm user ranh giới.
```

### 10.20. Admin khóa nhầm user VIP

```
Tình huống:
  Admin Support (có quyền ADM-02) nhầm lẫn SUSPENDED user V.
  User V có riskScore = 20 (low), VIP, đã chơi 100+ lobby thành công.

Xử lý:
  User V mở app → suspended.
  User V khiếu nại ngay (BR-RISK-10).
  Admin Senior review ticket → thấy nhầm lẫn.
  → ADM-06 ghi chú, không ADM-05 (Senior reset).
  → Set accountStatus = active (manual update).
  → Gửi compensation: 50.000 BVC + apology notification.
  → Ghi audit log: actionBy=SENIOR_ID, reason="Admin Support nhầm lẫn".

Phòng ngừa tương lai:
  - ADM-02, ADM-03 yêu cầu nhập lý do bắt buộc.
  - Confirmed action từ Senior mới có hiệu lực (2-step verification).
  - Log tất cả action ADM-02/03/04 vào admin audit log riêng.
```

---

## 11. Thay đổi đối với API hiện tại

### 11.1. Endpoint mới

```
# Wallet
POST   /api/v1/wallet/topup
GET    /api/v1/wallet
GET    /api/v1/wallet/transactions

# Reservation
POST   /api/v1/reservations/quote
POST   /api/v1/reservations/confirm         # atomic: hold BVC + hold seat + hold game + create lobby
GET    /api/v1/reservations/pending-cafe-approval          # Cafe Manager: danh sách lobby chờ duyệt (BR-NEW-11)
GET    /api/v1/reservations/{id}/cafe-approval              # Cafe Manager: chi tiết 1 reservation pending (BR-NEW-11)
POST   /api/v1/reservations/{id}/cafe-approval             # Cafe Manager: duyệt/từ chối lobby (BR-NEW-11)

# Lobby
GET    /api/v1/lobbies/{id}/availability    # remaining slots, can-join
GET    /api/v1/lobbies/discoverable         # lobby công khai (bổ sung excludeSelfOverlapping)
POST   /api/v1/lobbies/search               # bổ sung excludeSelfOverlapping
```

### 11.2. Endpoint cần sửa

```
POST /api/v1/lobbies
  - Không còn là entry point chính.
  - Chỉ được gọi nội bộ từ reservation/confirm.

POST /api/v1/lobbies/{id}/lock
  - Có thể bỏ hoặc thay bằng "MarkReady" khi đạt minPlayers.

GET /api/v1/lobbies/search
  - Bổ sung filter "excludeSelfOverlapping" để tránh gợi ý lobby trùng lịch (BR-USER-LIMIT-02).

POST /api/v1/lobbies/{id}/cancel
  - Bổ sung logic refund theo BR-REFUND-02/03.
```

### 11.3. API BR-NEW-11: Cafe Approval

Các API phục vụ workflow cafe duyệt lobby trước khi publish (áp dụng cho lobby có `playDate` cách hiện tại ≥ 2 ngày).

| Endpoint | Method | Role | Mô tả |
|----------|--------|------|--------|
| `/reservations/pending-cafe-approval` | GET | Cafe Manager | Danh sách lobby đang chờ duyệt |
| `/reservations/{id}/cafe-approval` | GET | Cafe Manager | Chi tiết 1 reservation pending |
| `/reservations/{id}/cafe-approval` | POST | Cafe Manager | Chấp nhận (`approve: true`) hoặc từ chối (`approve: false`) |

**Luồng xử lý:**

1. Host tạo lobby với `playDate` > 2 ngày → Lobby status = `PendingCafeApproval`
2. Cafe Manager nhận notification → Gọi `GET /pending-cafe-approval` để xem danh sách
3. Cafe Manager xem chi tiết `GET /{id}/cafe-approval`
4. Cafe Manager duyệt:
   - **Chấp nhận** (`approve: true`): Lobby → `Open`, public cho members join
   - **Từ chối** (`approve: false`): Lobby → `RejectedByCafe`, refund 100% BVC cho host

**Response chi tiết reservation** bao gồm `CafeRejectionReason` để player xem lý do từ chối khi `status = CancelledByCafe`.

### 11.4. Schema thay đổi

`LobbyEntity`:

```
+ reservationId (required, FK)
+ depositSnapshot (ratePerPerson, maxPlayers, baseDeposit, riskMultiplier, finalDeposit)
+ cafeApprovalDeadline (datetime, nullable)
+ cafeRejectionReason (string, nullable)
+ approvedByCafeManagerId (FK, nullable)
```
+ playDate (required, DateOnly)        // ngày dự kiến chơi, BR-NEW-04
+ timeSlot (required, enum)            // morning | afternoon | evening | lateNight, BR-NEW-15
+ preferredStartTime (optional, TimeOfDay)  // giờ dự kiến trong timeSlot, BR-NEW-15b
+ recruitmentDeadline (computed, DateTime)  // = (playDate + timeSlot.startTime) - leadTime
+ status (enum)                        // bổ sung: pendingCafeApproval, rejectedByCafe, expiredByCafe
+ minDeposit (BVC)                     // minDeposit theo BR-NEW-01
- bookingId (có thể bỏ vì giờ reservationId đóng vai trò này)
- scheduledTime (thay bằng playDate + timeSlot)
```

`ReservationEntity` (mới):

```
id
hostId
cafeId
gameId
playDate (DateOnly)
timeSlot (enum: morning | afternoon | evening | lateNight)
preferredStartTime (TimeOfDay, optional)
recruitmentDeadline (DateTime, computed)
minPlayers, maxPlayers
depositConfigSnapshot
depositAmount (BVC)
minDepositApplied (BVC, theo BR-NEW-01)
riskMultiplier
status (draft, awaitingDeposit, pendingCafeApproval, holding, confirmed, ...)
currentPlayers  // mirror từ lobby
lobbyId
createdAt, updatedAt
```

`WalletEntity` (mới):

```
userId
availableBalance
heldBalance
totalActiveDeposit  // mirror, dùng cho BR-USER-LIMIT-03
riskMultiplier
isCoolingOff (bool)
coolingOffExpiresAt (DateTime, nullable)
```

`TransactionEntity` (mới):

```
id
userId
type (TOP_UP, DEPOSIT_HOLD, DEPOSIT_RELEASE, ...)
amount (BVC, luôn dương)
relatedBookingId
relatedLobbyId
idempotencyKey
createdAt
balanceSnapshot
```

`CafeConfigEntity` (mới, BR-NEW-12):

```
cafeId
capacity
maxLobbiesPerUserPerDay
maxPlayersPerLobbySameDay
maxPlayersPerLobby1Day
maxPlayersPerLobby2Days
maxPlayersPerLobby3To4Days
maxPlayersPerLobby5To7Days
minDepositSameDay
minDeposit1Day
minDeposit2Days
minDeposit3To4Days
minDeposit5To7Days
requireApprovalForDistant
distantThresholdDays
approvalTimeoutHours
maxTotalDepositPerUser
recruitmentDeadlineBufferMinutes
cancellationGraceMinutes
```

`PlayerRiskScoreEntity` (mới, BR-RISK-01):

```
userId (PK)
riskScore (0-100)
riskLevel (low | medium | high | critical)
lastUpdated
signals (JSONB: { SIG-01: 5, SIG-03: 750000, ... })
accountStatus (active | warning | restricted | suspended | banned)
adminNote (text, nullable)
adminActionBy (userId, nullable)
adminActionAt (datetime, nullable)
```

`PlayerAlertEntity` (mới, mục 18.8):

```
id (UUID, PK)
userId (FK)
alertType (auto_threshold_crossed | multi_account_detected | manual_report | admin_flagged)
severity (info | warning | critical)
signals (array of signal IDs)
createdAt
acknowledgedBy (userId admin, nullable)
acknowledgedAt (datetime, nullable)
status (open | acknowledged | resolved | dismissed)
resolutionNote (text, nullable)
```

`PlayerAccountLinkEntity` (mới, BR-RISK-08):

```
id (UUID, PK)
primaryUserId (FK)
linkedUserIds (array of userId)
detectionSignals (array of signal IDs)
detectedAt
reviewedBy (userId admin, nullable)
reviewedAt (datetime, nullable)
status (suspected | confirmed | dismissed)
```

`PlayerActionHistoryEntity` (mới, BR-RISK-05):

```
id (UUID, PK)
userId (FK)
actionType (warn | suspend_7d | suspend_30d | ban | reset_score | verify_required | multi_account_confirmed)
actionBy (userId admin hoặc "system")
reason (text)
metadata (JSONB: { beforeScore: 78, afterScore: 0, signals: [...] })
createdAt
expiresAt (datetime, nullable)
```

`RiskScoreHistoryEntity` (mới, BR-RISK-11):

```
userId (FK)
riskScore (0-100)
riskLevel (enum)
snapshotDate (date, partition key theo tháng)
signals (JSONB snapshot)

Partition:
  - Partition theo snapshotDate (range partitioning).
  - Retention: 365 ngày.
  - Sau 365 ngày: aggregate thành risk_score_history_summary (min/max/avg theo tháng).
```

---

## 12. Thay đổi đối với UI hiện tại

### 12.1. Màn hình mới

- **WalletPage**: hiển thị `availableBalance`, `heldBalance`, nút "Nạp BVC".
- **TopUpPage**: chọn số tiền hoặc gói, mở gateway.
- **TransactionHistoryPage**: lịch sử các ledger entry.
- **ReservationQuotePage**: trước khi tạo lobby, hiển thị chi tiết cọc.
- **DepositConfirmDialog**: trước khi trừ BVC, xác nhận lần cuối.

### 12.2. Màn hình lobby thay đổi

- LobbyPage nhận lobbyId + reservationId.
- Hiển thị thông tin cọc ở header.
- Trước deadline: hiển thị countdown.
- Sau deadline + đủ người: chuyển sang "Đã xác nhận".

### 12.3. Màn hình booking thay đổi

- BookingSummaryPage không còn là entry point.
- Thay vào đó là ReservationQuotePage (xem mục 12.1).
- BookingSuccessPage vẫn giữ nhưng chỉ cho trường hợp solo booking (chơi một mình, không qua lobby).

### 12.4. Component chung

- `BalanceDisplay`: hiển thị số dư BVC theo format chuẩn.
- `DepositBreakdownCard`: hiển thị chi tiết cọc (rate × maxPlayers × riskMultiplier).
- `DepositConfirmDialog`: popup cuối cùng trước khi xác nhận.
- `LobbyDepositBadge`: hiển thị cọc ngay trên lobby card.

---

## 13. Checklist triển khai

### Phase 1: Wallet

- [ ] Định nghĩa entity Wallet, Transaction.
- [ ] Mock datasource cho top-up.
- [ ] UI WalletPage, TopUpPage.
- [ ] API mock hoặc thật cho top-up.
- [ ] Ledger entry type TOP_UP.

### Phase 2: Reservation Quote

- [ ] Định nghĩa entity ReservationQuote.
- [ ] Backend tính quote với riskMultiplier.
- [ ] UI ReservationQuotePage.
- [ ] Hiển thị rõ BVC còn thiếu và nút top-up nhanh.

### Phase 3: Atomic Confirm

- [ ] Backend implement atomic transaction: hold BVC + hold seat + hold game + create reservation + create lobby.
- [ ] Outbox cho event "LobbyActivated".
- [ ] Client xử lý idempotencyKey.
- [ ] Test concurrency bằng cách gửi nhiều request đồng thời.

### Phase 4: Lobby flow update

- [ ] Bỏ `_triggerAutoBooking()`.
- [ ] Lobby full chỉ phát event, không tạo booking.
- [ ] Lobby deadline scheduler check BR-LOBBY-01.
- [ ] Refund timeout theo BR-REFUND-01.

### Phase 5: Cancellation & No-show

- [ ] BR-REFUND-02 theo mốc thời gian.
- [ ] BR-REFUND-03 grace 15 phút.
- [ ] No-show scheduler quét sau grace.
- [ ] Cập nhật Karma theo từng trường hợp.

### Phase 6: Check-in & Revenue

- [ ] POS scan QR, validate booking.
- [ ] Capture BVC deposit về doanh thu quán.
- [ ] Mở cửa sổ đánh giá Karma.

### Phase 7: Hardening

- [ ] Audit log cho mọi thay đổi status.
- [ ] Retry outbox worker.
- [ ] Test concurrency rộng.
- [ ] Test refund nhiều lần (idempotency).
- [ ] Test app resume flow.

---

## 14. Câu hỏi đã được chốt

| # | Câu hỏi | Quyết định |
|---|----------|-------------|
| 1 | Ai trả cọc? | Host trả toàn bộ |
| 2 | Giữ minPlayers hay maxPlayers ghế? | Giữ maxPlayers |
| 3 | Game copy có giữ không? | Có, giữ 1 copy |
| 4 | Hoàn bao nhiêu khi timeout? | 100% |
| 5 | Hoàn bao nhiêu khi host hủy? | 100% / 50% / 0% theo mốc 24h, 6h |
| 6 | Thành viên rời lobby? | Không ảnh hưởng cọc host |
| 7 | Cọc thuộc về ai? | 100% quán |
| 8 | Có rút BVC không? | Không |
| 9 | Max lobby active? | 1 host, 2 tổng |
| 10 | Lịch chồng lấn? | Không cho phép |
| 11 | Đạt minPlayers là đủ? | Đúng, không cần maxPlayers |
| 12 | Quán hủy? | Hoàn 100%, không bồi thường |
| 13 | Nạp nhưng reservation fail? | BVC giữ trong ví |
| 14 | Cách tính cọc? | ratePerPerson × maxPlayers × riskMultiplier |
| 15 | Dùng `playDate` riêng hay `scheduledTime` chính xác? | Dùng `playDate` (chỉ ngày) + `timeSlot` (morning/afternoon/evening/lateNight) |
| 16 | `preferredStartTime` có bắt buộc? | Optional, tham chiếu trong khoảng `[timeSlot.startTime, timeSlot.endTime]` |
| 17 | `timeSlot` được định nghĩa thế nào? | 4 khung giờ cố định: morning (06-12), afternoon (12-17), evening (17-23), lateNight (23-06, qua đêm) |
| 18 | Buffer tối thiểu từ tạo đến deadline? | 120 phút. < 60 phút → từ chối. 60-120 phút → cảnh báo. |
| 19 | `playDate` tối đa bao xa? | 7 ngày trong tương lai |
| 20 | Phân biệt hạn mức theo khoảng cách playDate? | Có. maxPlayers và minDeposit thay đổi theo khoảng cách (BR-NEW-01). |
| 21 | Player join lobby có thể host lobby khác? | Không (BR-USER-LIMIT-04). Phải đợi lobby hiện tại terminal. |
| 22 | Host có lobby có thể join lobby khác? | **Có** (BR-USER-LIMIT-05 ĐÃ BỎ), nếu không overlap lịch và tổng ≤ 2. |
| 23 | Cap tổng cọc active / user? | 500k user thường, 1tr VIP, 200k risk cao (BR-USER-LIMIT-03). |
| 24 | 1 lobby / cafe / user / playDate+timeSlot? | Có (BR-NEW-08). Tránh chiếm nhiều slot cùng quán. |
| 25 | Cooling-off nếu fail nhiều? | Có (BR-NEW-10). 3 lần fail / 7 ngày → cooling-off 30 ngày, cọc ×2. |
| 26 | Cafe duyệt lobby > 2 ngày? | Có (BR-NEW-11). Pending 24 giờ, không duyệt → hoàn cọc. |
| 27 | Cafe có thể cấu hình hạn mức riêng? | Có (BR-NEW-12). Mặc định theo BR-NEW-01. |
| 28 | Notification mốc nào? | 48h / 24h trước deadline + 2h / 30p trước playDate (nếu có preferredStartTime). |
| 29 | Phạm vi MVP cho player risk alert? | 6 signals cốt lõi (SIG-01/03/04/05/06/08), 4 admin actions (ADM-01/02/05/06). |
| 30 | Multi-account detection trong MVP? | Có: detect + alert, admin review thủ công. Auto-flag tài khoản mới ở phase sau. |
| 31 | User có thấy riskScore chi tiết? | Không. Chỉ thấy `riskLevel` (low/medium/high/critical) + giải thích chung (BR-RISK-09). |
| 32 | Cooling-off có kết hợp riskScore? | Có (BR-RISK-03). Cooling-off nhân cọc ×2 dựa trên riskScore hiện tại. |
| 33 | Admin role tách thành mấy mức? | 3 roles: Support + Risk + Senior (BR-RISK-07). |
| 34 | User bị khóa có khiếu nại? | `suspended` khiếu nại được, admin review 48 giờ (BR-RISK-10). `banned` không khiếu nại. |
| 35 | Lưu lịch sử riskScore bao lâu? | 365 ngày, partition theo tháng (BR-RISK-11). |
| 36 | Dashboard admin real-time? | Poll mỗi 30 giây (không SignalR cho MVP+1). |
| 37 | Report user từ user khác? | Không MVP+1. Tính năng report chỉ trong lobby (SIG-10 = 0). |

---

## 15. Phụ lục: thuật ngữ

| Thuật ngữ | Định nghĩa |
|-----------|------------|
| BVC | BoardVerse Coin. 1 BVC = 1.000 VND. |
| availableBalance | Số BVC trong ví có thể dùng để đặt cọc. |
| heldBalance | Số BVC đang bị giữ cho một reservation. |
| Host | Người tạo lobby, trả toàn bộ cọc. |
| Member | Người tham gia lobby do host khác tạo. |
| Lobby | Phòng chờ online để tuyển người chơi. |
| Reservation | Bản ghi giữ chỗ ngồi + game copy + BVC. |
| recruitmentDeadline | Thời điểm cuối cùng lobby tuyển đủ minPlayers. |
| riskMultiplier | Hệ số nhân cọc dựa trên lịch sử user. |
| Atomic transaction | Một giao dịch DB mà hoặc tất cả các bước đều thành công, hoặc tất cả đều rollback. |
| Outbox | Bảng tạm lưu event để đảm bảo phát event sau khi commit DB. |
| Idempotency key | Mã duy nhất để chống xử lý trùng yêu cầu. |
| playDate | Ngày dự kiến chơi (chỉ ngày, không có giờ). |
| timeSlot | Khung giờ cố định trong ngày: morning / afternoon / evening / lateNight. |
| preferredStartTime | Giờ dự kiến bắt đầu (optional, tham chiếu trong timeSlot). |
| scheduledTime | Thời điểm chính xác = `playDate + timeSlot.startTime`. |
| leadTimeMinutes | Số phút chuẩn bị trước scheduledTime (mặc định 20 phút). |
| Buffer | `recruitmentDeadline - now()`, số phút host có để tuyển người. |
| Cooling-off | Trạng thái hạn chế host khi fail nhiều lobby liên tiếp. |
| pendingCafeApproval | Trạng thái lobby chờ cafe duyệt (BR-NEW-11). |
| rejectedByCafe | Lobby bị cafe từ chối duyệt. |
| expiredByCafe | Lobby quá hạn duyệt 24 giờ, hoàn cọc. |
| CafeConfig | Cấu hình hạn mức riêng của từng cafe (BR-NEW-12). |

---

## 16. Tài liệu liên quan

- `infomation_project.md` – bối cảnh đồ án, phạm vi sản phẩm.
- `lobby_management/docs.md` – thiết kế module lobby (cần cập nhật theo tài liệu này).
- `booking_payment/docs.md` – thiết kế module booking (cần tách thành reservation + booking).
- `matchmaking_discovery/docs.md` – thiết kế module matchmaking (cần cập nhật flow createLobby).
- `mobile_architeture.md` – kiến trúc tổng thể Flutter mobile.
- `swagger.json` – đặc tả API.

---

## 17. Recruitment Window & Spam Prevention

> **Mục đích**: Tổng hợp các BR-NEW-* và cross-role rules bổ sung sau khi chốt với giảng viên. Section này là **canonical reference** cho các nghiệp vụ liên quan đến: thời gian tuyển người, chống spam, chống chiếm chỗ, cấu hình cafe.

### 17.1. Tổng quan các BR-NEW-*

| ID | Nội dung | Mục đích |
|----|----------|----------|
| BR-NEW-01 | Phân biệt maxPlayers + minDeposit theo khoảng cách playDate | Lobby càng xa, hạn mức càng chặt |
| BR-NEW-02 | 1 lobby active / playDate / user | Tránh spam slot trong ngày |
| BR-NEW-05 | Tối đa 5 lần tạo/hủy / playDate | Chống gian lận cọc |
| BR-NEW-08 | 1 lobby active / playDate+timeSlot / cafe / user | Tránh chiếm nhiều slot cùng quán |
| BR-NEW-10 | Cooling-off khi fail 3 lần / 7 ngày | Hạn chế user abuse |
| BR-NEW-11 | Cafe duyệt lobby > 2 ngày | Cafe kiểm soát lịch |
| BR-NEW-12 | Cafe cấu hình hạn mức riêng | Linh hoạt theo quán |
| BR-NEW-13 | Notification 4 mốc (48h/24h/2h/30p) | Nhắc host và member |
| BR-NEW-14 | Cảnh báo lobby có nguy cơ fail | Hỗ trợ host can thiệp sớm |
| BR-NEW-15 | Định nghĩa `timeSlot` (4 khung giờ cố định) | Chuẩn hóa thời gian |
| BR-NEW-15a | Công thức tính deadline từ playDate + timeSlot | Tính toán thống nhất |
| BR-NEW-15b | `preferredStartTime` tham chiếu trong timeSlot | Optional giờ chính xác |
| BR-USER-LIMIT-03 | Cap tổng cọc held (500k thường / 1tr VIP / 200k risk cao) | Chống spam cọc |
| BR-USER-LIMIT-04 | Player join lobby → không được host lobby khác | Cross-role rule |
| BR-USER-LIMIT-05 | **ĐÃ BỎ** — Host được phép join lobby khác nếu không overlap | Cross-role rule |

### 17.2. Bảng hạn mức theo khoảng cách `playDate` (BR-NEW-01)

| Khoảng cách `playDate` | `maxPlayers` tối đa | `minDeposit` (BVC) | Yêu cầu bổ sung |
|------------------------|---------------------|---------------------|-------------------|
| Hôm nay (cùng ngày) | 30 | 50.000 | Buffer ≥ 120 phút |
| 1 ngày sau | 20 | 50.000 | Buffer ≥ 120 phút |
| 2 ngày sau | 15 | 100.000 | Cần cafe duyệt nếu `maxPlayers` > 10 |
| 3–4 ngày sau | 10 | 150.000 | Cần cafe duyệt |
| 5–7 ngày sau | 6 | 200.000 | Cần cafe duyệt |

Ví dụ tính `minDeposit`:

```
playDate = hôm nay (cùng ngày)
maxPlayers = 6
minDeposit = 50.000 BVC (hôm nay)
finalDeposit = max(minDeposit, depositRatePerPerson × maxPlayers × riskMultiplier)

Cafe cấu hình: 20 BVC/người
riskMultiplier = 1.0
finalDeposit = max(50.000, 20 × 6 × 1.0) = max(50.000, 120.000) = 120.000 BVC
```

```
playDate = 5 ngày sau
maxPlayers = 6
minDeposit = 200.000 BVC (5-7 ngày)
finalDeposit = max(200.000, 20 × 6 × 1.0) = max(200.000, 120.000) = 200.000 BVC
```

### 17.3. Công thức tính `recruitmentDeadline` (BR-LOBBY-01, BR-NEW-15a)

```
scheduledTime = playDate + timeSlot.startTime
recruitmentDeadline = scheduledTime - leadTimeMinutes
```

Trong đó:

- `playDate`: kiểu DateOnly (chỉ ngày).
- `timeSlot.startTime`:

| `timeSlot` | `startTime` | `endTime` |
|------------|-------------|-----------|
| `morning` | 09:00 | 13:00 |
| `afternoon` | 13:00 | 18:00 |
| `evening` | 18:00 | 23:00 |
| `lateNight` | 23:00 | 06:00 |

- `leadTimeMinutes`: mặc định **20 phút** (lấy từ deposit config của cafe).

Ví dụ:

```
playDate = 09/08/2026 (Chủ nhật)
timeSlot = afternoon (start 13:00)
leadTimeMinutes = 20

→ scheduledTime = 09/08/2026 13:00
→ recruitmentDeadline = 09/08/2026 12:40
```

### 17.4. Buffer validation (BR-LOBBY-01a/b/c)

| Điều kiện | Xử lý |
|-----------|--------|
| `buffer ≥ 120 phút` | Cho phép tạo bình thường |
| `60 phút ≤ buffer < 120 phút` | Cảnh báo UI, cho phép tạo (host tự chịu trách nhiệm) |
| `buffer < 60 phút` | Từ chối tạo lobby, yêu cầu chọn `timeSlot` khác |

Trong đó `buffer = recruitmentDeadline - now()`.

### 17.5. Trạng thái lobby bổ sung

```
pendingActivation      ← giao dịch atomic đang xử lý, lobby chưa publish
pendingCafeApproval    ← lobby > 2 ngày chờ cafe duyệt (BR-NEW-11)
open                   ← đang tuyển người, recruitmentDeadline chưa tới
viable                 ← đạt minPlayers, vẫn có thể nhận thêm
full                   ← đạt maxPlayers, ngừng nhận
inProgress             ← đã check-in tại quán
closed                 ← phiên chơi kết thúc, có thể mở cửa sổ đánh giá Karma
timeoutFailed          ← deadline trôi qua, không đủ người
hostCancelled          ← host hủy chủ động
rejectedByCafe         ← cafe từ chối duyệt (BR-NEW-11)
expiredByCafe          ← cafe không duyệt trong 24 giờ (BR-NEW-11)
```

### 17.6. Workflow duyệt lobby của cafe (BR-NEW-11)

```
Host tạo lobby (playDate > 2 ngày)
  → status = pendingCafeApproval
  → Cafe nhận notification (in-app + POS)
  ↓
  ├── Cafe nhấn "Duyệt" → status = open → lobby công khai
  ├── Cafe nhấn "Từ chối" → status = rejectedByCafe → hoàn 100% BVC
  └── 24 giờ không phản hồi → status = expiredByCafe → hoàn 100% BVC
```

Lưu ý:

- Lobby có `playDate` ≤ 2 ngày: không cần duyệt, tự động vào `open`.
- Cafe có thể cấu hình `distantThresholdDays` (mặc định 2 ngày) và `requireApprovalForDistant` (mặc định true) theo BR-NEW-12.
- Nếu cafe tắt `requireApprovalForDistant`: mọi lobby tự động `open`, không cần duyệt.

### 17.7. Cooling-off (BR-NEW-10)

Khi nào kích hoạt:

- Lobby `timeoutFailed` liên tiếp **3 lần** trong **7 ngày**.
- Lobby `hostCancelled` (sau grace) liên tiếp **3 lần** trong **7 ngày**.
- Tổng cọc forfeit/no-show vượt **500.000 BVC** trong **30 ngày**.

Hành vi khi cooling-off:

- User không được tạo lobby có `playDate` > 1 ngày trong tương lai.
- Cọc nhân **×2** cho mọi lobby mới (kết hợp với `riskMultiplier` hiện tại).
- Thời hạn cooling-off: **30 ngày**, sau đó hệ thống tự đánh giá lại.
- Trong thời gian cooling-off, nếu user tiếp tục fail → gia hạn thêm 30 ngày, cọc ×3.

Trường `WalletEntity.isCoolingOff = true` và `coolingOffExpiresAt` được set.

### 17.8. CafeConfig mặc định (BR-NEW-12)

```yaml
cafe_config:
  capacity: 30
  max_lobbies_per_user_per_day: 1
  max_players_per_lobby_same_day: 30
  max_players_per_lobby_1_day: 20
  max_players_per_lobby_2_days: 15
  max_players_per_lobby_3_to_4_days: 10
  max_players_per_lobby_5_to_7_days: 6
  min_deposit_same_day: 50000
  min_deposit_1_day: 50000
  min_deposit_2_days: 100000
  min_deposit_3_to_4_days: 150000
  min_deposit_5_to_7_days: 200000
  require_approval_for_distant: true
  distant_threshold_days: 2
  approval_timeout_hours: 24
  max_total_deposit_per_user: 500000
  recruitment_deadline_buffer_minutes: 120
  cancellation_grace_minutes: 15
```

Cafe có thể override từng giá trị. Các giá trị cafe config **không được vượt** giới hạn an toàn toàn hệ thống:

- `max_total_deposit_per_user` ≤ 1.000.000 BVC.
- `distant_threshold_days` ≥ 1.
- `recruitment_deadline_buffer_minutes` ≥ 60.

### 17.9. Cross-role rules (BR-USER-LIMIT-04, 05)

#### Quy tắc 1: Player join lobby → không được host lobby khác

```
User A đã join lobby XYZ làm member.
User A muốn tạo lobby mới làm host.
→ Từ chối (BR-USER-LIMIT-04).

Ngoại lệ: nếu lobby XYZ đã terminal hoặc user A đã rời lobby trước recruitmentDeadline.
```

#### Quy tắc 2: Host có lobby → **ĐƯỢC PHÉP join lobby khác** (BR-USER-LIMIT-05 ĐÃ BỎ)

```
User B đã host lobby ABC.
User B muốn join lobby DEF làm member.

Điều kiện để được join:
  (a) Tổng lobby: host + member ≤ 2 (BR-USER-LIMIT-01)
      - Nếu B chưa là member lobby nào (tổng = 1) → CHO PHÉP
      - Nếu B đã là member 1 lobby khác (tổng = 2) → TỪ CHỐI
  (b) Không overlap: lobby DEF không trùng lịch với lobby ABC (+30 phút buffer)
      - Nếu không overlap → CHO PHÉP
      - Nếu overlap → TỪ CHỐI

Ví dụ hợp lệ:
  - Host lobby thứ 7 (3 ngày sau) → Join lobby hôm nay ✓
  - Host lobby tối mai → Join lobby sáng mai ✓

Ví dụ bị từ chối:
  - Host lobby 19:00 hôm nay → Join lobby 19:00 hôm nay (overlap) ✗
  - Host lobby 19:00 + đã member 1 lobby khác (tổng = 2) ✗
```

#### Quy tắc 3: Tổng 2 lobby active

```
User C đang host lobby 1.
User C muốn tạo lobby 2 mới.
→ Từ chối (BR-USER-LIMIT-01 đạt max 2 lobby).

User C đang host lobby 1 và đã member lobby 2.
→ Đã đạt max 2 lobby, không thể tạo lobby 3.
```

### 17.10. Notification schedule (BR-NEW-13)

| Mốc | Người nhận | Nội dung |
|-----|-----------|----------|
| 48 giờ trước `recruitmentDeadline` | Host | "Lobby XYZ còn 48 giờ để tuyển đủ người. Hiện có X/Y." |
| 24 giờ trước `recruitmentDeadline` | Host + Members | "Lobby XYZ sắp đến deadline. Còn thiếu X người." |
| 2 giờ trước `preferredStartTime` (nếu có) | Host + Members | "Lobby XYZ bắt đầu sau 2 giờ tại Cafe Y." |
| 30 phút trước `preferredStartTime` (nếu có) | Host + Members | "Lobby XYZ bắt đầu sau 30 phút. Đừng quên nhé!" |

Nếu lobby không có `preferredStartTime`: bỏ qua các mốc 2 giờ và 30 phút.

### 17.11. Cảnh báo lobby có nguy cơ fail (BR-NEW-14)

Điều kiện: sau **50% thời gian tuyển** (từ lúc tạo đến `recruitmentDeadline`), lobby có `< 50% minPlayers`.

```
Ví dụ:
  playDate = 09/08
  timeSlot = afternoon (start 13:00)
  recruitmentDeadline = 09/08 12:40
  Tạo lobby lúc 02/08 10:00
  Thời gian tuyển = 7 ngày 2 giờ 40 phút
  50% thời gian tuyển = 02/08 10:00 + 3 ngày 13 giờ 20 phút = 05/08 23:20

  Nếu đến 05/08 23:20 mà lobby có 1/4 người (minPlayers = 4):
    → Gửi notification cho host
    → Đề xuất:
      (a) Chia sẻ link mời bạn qua Zalo/Messenger
      (b) Đổi sang timeSlot khác xa hơn (cùng playDate hoặc playDate khác)
      (c) Hủy lobby (hoàn cọc theo BR-REFUND-03)
```

### 17.12. Phân tích kịch bản spam cọc chiếm chỗ (đa lớp bảo vệ)

Kịch bản: Attacker tạo user mới, spam 28 lobby × 50k BVC × maxPlayers = 30.

| Lớp bảo vệ | BR | Hiệu quả |
|-------------|-----|----------|
| Cap tổng cọc active | BR-USER-LIMIT-03 | Sau 10 lobby × 50k = 500k → đạt cap. |
| 1 lobby / playDate+timeSlot / cafe / user | BR-NEW-08 | Sau lobby đầu tiên ở Cafe A, slot morning, ngày X → lobby thứ 2 cùng slot bị từ chối. |
| Hạn mức theo khoảng cách | BR-NEW-01 | Lobby > 2 ngày: maxPlayers = 6-15 (giảm từ 30). |
| Cafe duyệt > 2 ngày | BR-NEW-11 | Cafe phát hiện pattern spam → từ chối. |
| Cooling-off | BR-NEW-10 | Sau 3 lobby fail → cooling-off 30 ngày, cọc ×2, chỉ đặt trong ngày. |

Kết luận: thay vì spam 28 lobby, attacker chỉ spam được **2-3 lobby** trước khi bị chặn đa lớp.

### 17.13. Lưu ý khi triển khai MVP

- `leadTimeMinutes` mặc định **20 phút** (giữ nguyên từ phiên bản trước).
- `Buffer` mặc định **120 phút** (BR-LOBBY-01a).
- `timeSlot` là enum cố định, không cho phép cafe config thêm khung giờ mới (chỉ override tên hiển thị).
- `CafeConfig` chỉ cần thiết cho phase 2. MVP có thể dùng giá trị mặc định hard-coded.
- `pendingCafeApproval` workflow cần UI trên POS/Management Web, MVP có thể để admin duyệt thay.

---

## 18. Player Risk Alert & Admin Management

> **Mục đích**: Bổ sung hệ thống phát hiện, cảnh báo và xử lý player có hành vi bất thường (spam cọc, spam lobby, multi-account). Kết hợp với BR-NEW-* đã có (phòng thủ tự động) để tạo phòng thủ đa lớp với can thiệp thủ công của admin.

### 18.1. Các chỉ số (signals) theo dõi

| Signal ID | Mô tả | Trọng số | Phát hiện |
|-----------|--------|----------|-----------|
| SIG-01 | Số lobby fail (`timeoutFailed`) trong 7 ngày | × 15 | Scheduled job |
| SIG-02 | Số lobby host cancel sau grace trong 7 ngày | × 15 | Scheduled job |
| SIG-03 | Tổng BVC forfeit/no-show trong 30 ngày | × 20 / 1000 (mỗi 100k) | Scheduled job |
| SIG-04 | Số lần tạo+hủy lobby liên tiếp cùng `playDate` (BR-NEW-05) | × 10 | Real-time |
| SIG-05 | Số lần join/rời lobby trong 24 giờ | × 5 | Real-time |
| SIG-06 | Số lần bị từ chối tạo lobby (vượt cap, buffer, cafe từ chối) | × 8 | Real-time |
| SIG-07 | Số account/IP thiết bị trùng trong 30 ngày (multi-account) | × 30 | Scheduled job |
| SIG-08 | Tốc độ thao tác bất thường (tạo + cancel trong < 5 phút) | × 25 | Real-time |
| SIG-09 | Chênh lệch giờ hoạt động so với baseline | × -2 (giảm nhẹ) | Real-time |
| SIG-10 | Số lần nhận report từ user khác | × 20 | Real-time |

### 18.2. Công thức tính `riskScore`

**BR-RISK-01**: `riskScore` được tính theo công thức:

```
riskScore = clamp(
    SIG-01 × 15
  + SIG-02 × 15
  + SIG-03 × 20 / 1000
  + SIG-04 × 10
  + SIG-05 × 5
  + SIG-06 × 8
  + SIG-07 × 30
  + SIG-08 × 25
  + SIG-10 × 20
  - SIG-09 × 2
, 0, 100)
```

**Phân loại mức rủi ro:**

| Mức | riskScore | Hành vi hệ thống |
|-----|-----------|------------------|
| `low` | 0-29 | Bình thường, không chặn |
| `medium` | 30-49 | Cảnh báo UI nhẹ, không chặn |
| `high` | 50-74 | Cọc ×1.5, ghi nhận audit |
| `critical` | 75-100 | Cọc ×2, hạn chế tạo lobby, yêu cầu admin review |

**BR-RISK-02 (Auto-trigger khi cross mức)**: Hệ thống tự động ghi log và thông báo khi `riskScore` vượt mức:

- Cross 30: ghi audit log.
- Cross 50: hiển thị warning UI cho user.
- Cross 75: tạo admin alert, có thể tạm khóa.

### 18.3. Quan hệ giữa `riskScore` và `riskMultiplier` hiện tại

**BR-RISK-03 (Mapping)**:

```
riskMultiplier = 1.0 + (riskScore / 100) × 1.0

riskScore = 0    → riskMultiplier = 1.0
riskScore = 50   → riskMultiplier = 1.5
riskScore = 75   → riskMultiplier = 1.75
riskScore = 100  → riskMultiplier = 2.0
```

Khi BR-NEW-10 (cooling-off) active:

```
riskMultiplier = (1.0 + (riskScore / 100) × 1.0) × 2.0
```

Khi BR-RISK-03 critical active:

```
riskMultiplier = (1.0 + (riskScore / 100) × 1.0) × 1.5
+ Tạo admin alert
+ Hạn chế tạo lobby
```

### 18.4. Trạng thái tài khoản

```
active         ← user hoạt động bình thường
warning        ← có cảnh báo, vẫn dùng được (cọc ×2)
restricted     ← hạn chế (không tạo lobby, chỉ join)
suspended      ← tạm khóa (không vào app)
banned         ← khóa vĩnh viễn
```

**Bảng hậu quả theo trạng thái:**

| Trạng thái | Tạo lobby | Join lobby | Top-up | Login |
|------------|-----------|------------|--------|-------|
| active | ✓ | ✓ | ✓ | ✓ |
| warning | ✓ (cọc ×2) | ✓ | ✓ | ✓ |
| restricted | ✗ | ✓ | ✓ | ✓ |
| suspended | ✗ | ✗ | ✗ | ✗ |
| banned | ✗ | ✗ | ✗ | ✗ |

### 18.5. Admin Actions

**BR-RISK-04**: Admin có 7 actions để xử lý player:

| Action ID | Hành động | Áp dụng khi | Hiệu lực | Role tối thiểu |
|-----------|-----------|-------------|----------|---------------|
| ADM-01 | Gửi cảnh báo qua notification | riskScore cao nhưng chưa critical | Ngay | Support |
| ADM-02 | Tạm khóa 7 ngày | Spam vừa phải, cho cơ hội sửa | 7 ngày | Risk |
| ADM-03 | Tạm khóa 30 ngày | Spam nghiêm trọng | 30 ngày | Risk |
| ADM-04 | Khóa vĩnh viễn | Vi phạm nặng, multi-account | Vĩnh viễn | Senior |
| ADM-05 | Reset risk score | Admin xác nhận false positive | Ngay | Risk |
| ADM-06 | Ghi chú nội bộ | Đánh dấu để theo dõi | Ngay | Support |
| ADM-07 | Yêu cầu xác minh danh tính | riskScore high, cần thêm info | Đến khi user verify | Support |

**BR-RISK-05**: Mọi admin action phải ghi audit log vĩnh viễn, bao gồm:

- Action ID.
- Admin ID và role.
- Lý do (text).
- Metadata (JSONB: trước/sau `riskScore`, `riskLevel`, signals chi tiết).
- Thời điểm thực hiện.
- Thời điểm hết hạn (nếu có).

**BR-RISK-06**: Admin action có thời hạn (ADM-02, ADM-03) tự động hết hạn và revert account về `active`.

### 18.6. Admin Role & Permission

**BR-RISK-07**: Tách admin thành 3 roles:

| Role | Quyền |
|------|-------|
| Admin Support | ADM-01, ADM-06, ADM-07, xem dashboard, xem chi tiết |
| Admin Risk | Tất cả Support + ADM-02, ADM-03, ADM-05 |
| Admin Senior | Tất cả Risk + ADM-04 (khóa vĩnh viễn), multi-account action, reset risk score toàn hệ thống |

### 18.7. Multi-account Detection (SIG-07)

**Các dấu hiệu multi-account:**

1. Cùng IP đăng ký nhiều account trong 7 ngày.
2. Cùng device ID (Android ID, iOS Identifier, IMEI).
3. Cùng payment method (VNPay/MoMo account).
4. Cùng pattern hành vi (cùng giờ, cùng kiểu lobby).
5. Cùng SĐT hoặc email recovery.
6. Cùng vị trí địa lý thường trú.

**BR-RISK-08 (Detection rule)**: Nếu 2 account có ≥ 2 tín hiệu trùng trong 30 ngày:

```
→ Tạo record "suspected_multi_account"
→ Ghi admin alert (mức high)
→ Không auto-merge, admin review thủ công
```

**Khi admin xác nhận multi-account:**

- Hợp nhất `riskScore` của tất cả account con.
- Khóa tất cả account con ngoại trừ 1 account chính.
- Hoàn BVC held về account chính (nếu có).
- Ghi audit log vĩnh viễn.

### 18.8. Quy trình xử lý 1 alert

```
1. Auto detection phát hiện signal
   → Tạo PlayerAlert (status=open)
   → Notification cho admin team
   ↓
2. Admin review alert
   → Xem chi tiết, kiểm tra signals
   → Quyết định:
      ├── False positive → Dismiss + ghi chú (ADM-05, ADM-06)
      ├── Nghi ngờ nhẹ → Gửi cảnh báo user (ADM-01)
      ├── Spam vừa → Tạm khóa 7 ngày (ADM-02)
      ├── Spam nặng → Tạm khóa 30 ngày (ADM-03)
      └── Nghiêm trọng → Khóa vĩnh viễn (ADM-04)
   ↓
3. Cập nhật PlayerAlert (status=resolved)
   → Ghi PlayerActionHistory
   → Gửi notification cho user (nếu có)
   ↓
4. Audit log lưu vĩnh viễn
```

### 18.9. Scheduled jobs

| Job ID | Tần suất | Mục đích |
|--------|----------|----------|
| `risk_score_recompute` | Mỗi giờ | Tính lại riskScore cho tất cả user active |
| `signal_detect_multi_account` | Mỗi 6 giờ | Phát hiện multi-account mới |
| `alert_expiry_cleanup` | Mỗi ngày | Tự động đóng alert cũ |
| `suspension_expiry_check` | Mỗi giờ | Tự động mở khóa tài khoản hết hạn |

**Real-time detection (không đợi cron):**

- **SIG-04, SIG-05, SIG-06, SIG-08, SIG-10**: Phát hiện tại thời điểm user thực hiện hành động.

### 18.10. Frontend hiển thị cho user

**Khi riskScore vượt 50 (medium):**

```
⚠️ Bạn đang có dấu hiệu hoạt động bất thường.
   Một số lobby gần đây đã bị hủy hoặc không đạt đủ người.
   Tiền cọc của bạn có thể bị tăng để đảm bảo cam kết.
   
   [Tìm hiểu thêm] [Liên hệ hỗ trợ]
```

**Khi riskScore vượt 75 (critical):**

```
🚫 Tài khoản của bạn đang bị giới hạn.
   Bạn không thể tạo lobby mới.
   Vui lòng liên hệ hỗ trợ để được xem xét.
   
   [Liên hệ hỗ trợ]
```

**Khi cooling-off active:**

```
⏰ Bạn đang trong thời gian giới hạn (còn 25 ngày).
   Bạn chỉ có thể tạo lobby có playDate trong ngày.
   Tiền cọc được nhân đôi để đảm bảo cam kết.
```

**BR-RISK-09**: Chỉ hiển thị `riskLevel` (low/medium/high/critical) cho user, không hiển thị `riskScore` chi tiết và signals cụ thể. Tránh user "game the system".

### 18.11. Admin Web UI cấu trúc

```
web_admin/lib/features/risk_management/
├── domain/
│   ├── entities/
│   │   ├── risk_score_entity.dart
│   │   ├── risk_alert_entity.dart
│   │   ├── account_link_entity.dart
│   │   └── action_history_entity.dart
│   └── repositories/
│       └── risk_management_repository.dart
├── data/
│   └── ...
└── presentation/
    ├── cubit/
    │   ├── risk_dashboard_cubit.dart
    │   └── player_detail_cubit.dart
    ├── pages/
    │   ├── risk_dashboard_page.dart
    │   ├── player_risk_detail_page.dart
    │   └── multi_account_investigation_page.dart
    └── widgets/
        ├── risk_score_gauge.dart
        ├── signal_breakdown_card.dart
        ├── action_history_timeline.dart
        └── risk_trend_chart.dart
```

### 18.12. Dashboard layout

```
┌─────────────────────────────────────────────────────────┐
│ Player Risk Dashboard                                   │
├─────────────────────────────────────────────────────────┤
│ [Critical: 12]  [High: 47]  [Medium: 134]  [Low: 8.2k] │
├─────────────────────────────────────────────────────────┤
│ Filters: [Mức rủi ro ▼] [Trạng thái ▼] [Khu vực ▼]      │
│ Search: [username, email, SĐT...]                       │
├─────────────────────────────────────────────────────────┤
│ User          Score  Mức    Signals              Action  │
│ ────────────  ─────  ─────  ──────────────────   ────── │
│ user_a        85     CRIT   SIG-01,03,08        [Xem]   │
│ user_b        62     HIGH   SIG-01,04,06        [Xem]   │
│ user_c        45     MED    SIG-02,05           [Xem]   │
└─────────────────────────────────────────────────────────┘
```

**Update mode:**

- Dashboard poll mỗi **30 giây** (balance giữa real-time và server load).
- Chi tiết player auto-refresh khi có signal mới.

### 18.13. Chi tiết player

```
┌─────────────────────────────────────────────────────────┐
│ user_a — Risk Score: 85/100 (CRITICAL)                  │
├─────────────────────────────────────────────────────────┤
│ Thông tin cơ bản:                                       │
│   - User ID, email, SĐT, ngày đăng ký                   │
│   - Số lần đăng nhập, devices, IP lịch sử               │
│                                                         │
│ Risk timeline (30 ngày):                                │
│   [biểu đồ line chart riskScore theo ngày]              │
│                                                         │
│ Active signals:                                         │
│   ⚠ SIG-01: 5 lobby fail trong 7 ngày (cao)             │
│   ⚠ SIG-03: 750k BVC forfeit trong 30 ngày (cao)        │
│   ⚠ SIG-08: 12 lần tạo+hủy trong 5 phút (nghiêm trọng) │
│                                                         │
│ Lịch sử lobby:                                          │
│   [Bảng lobby với status, thời gian, số BVC, lý do]     │
│                                                         │
│ Lịch sử tài khoản:                                      │
│   [Devices, IP, login time, email/SĐT changes]          │
│                                                         │
│ Hành động:                                              │
│   [Cảnh báo] [Tạm khóa 7 ngày] [Khóa vĩnh viễn]         │
│   [Reset risk score] [Ghi chú admin] [Export CSV]        │
└─────────────────────────────────────────────────────────┘
```

### 18.14. User khiếu nại (Appeal)

**BR-RISK-10**: User bị `suspended` được khiếu nại qua ticket support:

- Tạo ticket với lý do + bằng chứng.
- Admin review trong **48 giờ**.
- 3 kết quả:
  - **Upheld**: account được mở khóa, reset risk score.
  - **Partially upheld**: giảm thời gian khóa, cảnh báo nhẹ.
  - **Rejected**: giữ nguyên quyết định ban đầu.

User bị `banned` không khiếu nại được trong MVP (có thể email trực tiếp admin).

### 18.15. Metrics & KPI cho admin

- Số alert mới/ngày.
- Thời gian xử lý alert trung bình (mục tiêu: < 24 giờ).
- Tỷ lệ alert false positive (mục tiêu: < 15%).
- Top 10 user có riskScore cao.
- Số user bị tạm khóa/ngày.
- Hiệu quả: tỷ lệ user bị tạm khóa cải thiện hành vi (không bị lại trong 90 ngày).

### 18.16. Storage & Retention

**Bảng `risk_score_history`**:

```
userId
riskScore
riskLevel
snapshotDate (chỉ ngày, partition theo tháng)
signals (JSONB snapshot)
```

**BR-RISK-11**: Lưu lịch sử `riskScore` **365 ngày** để audit và điều tra.

- Partition table theo tháng để tối ưu.
- Sau 365 ngày: aggregate thành summary tháng (min/max/avg) để tiết kiệm.

### 18.17. Phạm vi MVP

**MVP+1 (Phase tiếp theo, sau release BR-NEW-*):**

- Implement **6 signals cốt lõi** (SIG-01, SIG-03, SIG-04, SIG-05, SIG-06, SIG-08) - balance giữa MVP và production.
- Tính `riskScore` đơn giản theo BR-RISK-01.
- Admin dashboard cơ bản: xem danh sách + chi tiết.
- 4 admin actions: ADM-01, ADM-02, ADM-05, ADM-06.
- Auto-trigger khi `riskLevel` thay đổi.
- Dashboard poll mỗi 30 giây.

**Phase sau:**

- Thêm signals còn lại (SIG-02, SIG-07, SIG-09, SIG-10).
- Multi-account detection đầy đủ.
- 7 admin actions đầy đủ + 3 roles.
- User khiếu nại (appeal).
- Real-time alert qua SignalR.

**Không MVP:**

- ML-based anomaly detection.
- Biểu chỉnh bằng AI.

### 18.18. Tích hợp với hệ thống hiện có

**Bảng ánh xạ BR-NEW-* với BR-RISK-*:**

| BR cũ | BR mới | Quan hệ |
|-------|--------|---------|
| BR-NEW-10 (cooling-off) | BR-RISK-03 | Cooling-off input vào riskMultiplier |
| BR-NEW-08 (1 lobby/cafe) | BR-RISK-04 + ADM-04 | Lặp vi phạm có thể bị khóa |
| BR-USER-LIMIT-03 (cap cọc) | BR-RISK-04 + ADM-02 | Spam vượt cap có thể bị tạm khóa |

**Cập nhật WalletEntity (mục 11.3):**

```
userId
availableBalance
heldBalance
totalActiveDeposit
riskMultiplier                      // từ 1.0 - 2.0 (BR-RISK-03 mapping)
riskScore (0-100, mới)              // BR-RISK-01
riskLevel (low | medium | high | critical)
isCoolingOff
coolingOffExpiresAt
accountStatus (active | warning | restricted | suspended | banned)  // BR-RISK-04
```

---

**Trạng thái tài liệu**: Đã chốt với giảng viên và nhóm. Bổ sung section 17 (Recruitment Window & Spam Prevention) và section 18 (Player Risk Alert & Admin Management) ngày 02/08/2026 sau khi phân tích kỹ kịch bản spam cọc chiếm chỗ, cross-role rules và hệ thống phát hiện player spam. Mọi thay đổi phải được review và cập nhật đồng bộ tại file này.
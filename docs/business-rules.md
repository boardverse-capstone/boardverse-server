# Business Rules - BoardVerse

## 1. Đơn vị tiền tệ

| Thuật ngữ | Mô tả |
|---|---|
| **BVC** | BoardVerse Coin: 1 BVC = 1.000 VND |
| **availableBalance** | BVC trong ví có thể dùng đặt cọc/thanh toán |
| **heldBalance** | BVC đang bị giữ cho reservation/lobby |

## 2. Người dùng & Quyền

| Vai trò | Mô tả |
|---|---|
| **Player** | Người chơi thông thường |
| **Host** | Người tạo lobby, trả toàn bộ cọc |
| **Member** | Người tham gia lobby do host tạo |
| **Admin** | Quản trị hệ thống |

## 3. Đặt cọc (Deposit)

### 3.1 Ai trả cọc?
- **Host trả toàn bộ cọc** cho lobby
- Member không trả cọc khi tham gia lobby

### 3.2 Công thức tính cọc

```
finalDeposit = max(minDeposit(theo khoảng cách playDate),
                   depositRatePerPerson × maxPlayers × riskMultiplier)
```

### 3.3 Giới hạn
- depositRatePerPerson: **1 - 100 BVC/người**
- finalDeposit không vượt **50% giá vé giờ đầu**
- riskMultiplier: **1.0 - 2.0** (theo lịch sử user)

## 4. Lobby (Phòng chờ)

### 4.1 Trạng thái Lobby

| Trạng thái | Mô tả |
|---|---|
| `Open` | Đang tuyển người |
| `Viable` | Đạt minPlayers, vẫn nhận thêm |
| `Full` | Đạt maxPlayers |
| `WaitingCheckIn` | Tất cả members đã Ready |
| `InProgress` | Đã check-in tại quán |
| `Closed` | Phiên kết thúc |
| `TimeoutFailed` | Không đủ người đến deadline |
| `HostCancelled` | Host hủy lobby |
| `Dissolved` | Host hủy trước check-in |
| `PendingCafeApproval` | Chờ cafe duyệt (>2 ngày) |
| `RejectedByCafe` | Cafe từ chối duyệt |
| `ExpiredByCafe` | Cafe không duyệt trong 24h |

### 4.2 Giới hạn Lobby
- **maxPlayers** nằm trong [GameTemplate.MinPlayers, GameTemplate.MaxPlayers]
- **maxPlayers** giới hạn theo khoảng cách playDate:
  - Hôm nay: 30 người
  - 1 ngày sau: 20 người
  - 2 ngày sau: 15 người
  - 3-4 ngày sau: 10 người
  - 5-7 ngày sau: 6 người

### 4.3 Recruitment Deadline
```
recruitmentDeadline = playDate + preferredStartTime - leadTimeMinutes (mặc định 20 phút)
```

### 4.4 Buffer validation
| Buffer | Hành vi |
|---|---|
| ≥ 120 phút | Cho phép tạo |
| 60-120 phút | Cảnh báo, cho phép |
| < 60 phút | Từ chối tạo lobby |

## 5. Reservation (Giữ chỗ)

### 5.1 Trạng thái Reservation

| Trạng thái | Mô tả |
|---|---|
| `AwaitingDeposit` | Quote đã tạo, chờ confirm |
| `Holding` | Đã giữ BVC + ghế + game |
| `Confirmed` | Lobby đạt minPlayers |
| `CheckedIn` | POS đã quét QR |
| `InProgress` | Đang chơi |
| `Completed` | Hoàn tất đúng giờ |
| `EarlyCheckout` | Về sớm (playedRatio ≥ 50% → hoàn 30%) |
| `Expired` | Không đủ người đến deadline |
| `CancelledByPlayer` | Host hủy |
| `CancelledByCafe` | Cafe hủy (hoàn 100%) |
| `NoShow` | Không check-in sau grace period |

### 5.2 Atomic Transaction
Khi confirm reservation, thực hiện trong 1 transaction:
1. Trừ availableBalance, cộng heldBalance
2. INSERT reservation + lobby
3. HOLD ghế (SeatInventory)
4. HOLD game copy (GameInventory)
5. INSERT ledger DEPOSIT_HOLD

## 6. Check-in & Thanh toán

### 6.1 Check-in
- POS quét QR (ReservationCode)
- Validate thời gian nằm trong khung cho phép
- Gán số bàn

### 6.2 Công thức hóa đơn
```
Hóa đơn = Tiền giờ chơi + Phí phạt - Deposit đã đặt
```

### 6.3 Giữ ghế & Game
- Khi tạo lobby: giữ **maxPlayers** ghế
- Khi tạo lobby: giữ **1 game copy**

## 7. Hoàn cọc (Refund)

### 7.1 Timeout (không đủ người)
- Hoàn **100%** BVC
- Không phạt Karma

### 7.2 Host hủy
| Thời điểm | Hoàn | Karma |
|---|---|---|
| Grace 15 phút + chưa có member | 100% | Không |
| ≥ 24h trước giờ chơi | 100% | Không |
| 6-24h trước | 50% | Giảm nhẹ |
| < 6h trước | 0% | Giảm nặng |

### 7.3 Early checkout
| playedRatio | Hoàn BVC | Karma |
|---|---|---|
| < 50% | 0% (forfeit) | Giảm nhẹ |
| ≥ 50% | 30% | Không phạt |
| ≥ 90% | 0% (on-time) | Không phạt |

### 7.4 No-show
- Hoàn **0%** BVC
- Giảm Karma nặng

### 7.5 Cafe hủy
- Hoàn **100%** BVC
- Không phạt Karma

## 8. Giới hạn User

| Rule | Mô tả |
|---|---|
| **Max lobby active** | 1 host + 1 member = tổng 2 lobby |
| **Không lịch chồng** | 2 lobby/booking trùng thời gian +30 phút buffer |
| **Cap tổng cọc** | Thường: 500.000 BVC, VIP: 1.000.000 BVC, Risk cao: 200.000 BVC |
| **Cross-role** | Member không được host lobby khác |
| **Cross-role** | Host không được join lobby khác |

## 9. Cooling-off

### Kích hoạt khi
- Lobby `timeoutFailed` liên tiếp **3 lần** trong **7 ngày**
- Lobby `hostCancelled` sau grace **3 lần** trong **7 ngày**
- Tổng forfeit/no-show vượt **500.000 BVC** trong **30 ngày**

### Hành vi
- Không được tạo lobby có `playDate > 1 ngày`
- Cọc nhân **×2**
- Thời hạn **30 ngày**

## 10. Risk Score

### 10.1 Công thức
```
riskScore = SIG-01×15 + SIG-02×15 + SIG-03×20/1000 + SIG-04×10
          + SIG-05×5 + SIG-06×8 + SIG-07×30 + SIG-08×25 + SIG-10×20 - SIG-09×2
```

### 10.2 Phân mức
| Mức | Score | Hành vi |
|---|---|---|
| Low | 0-29 | Bình thường |
| Medium | 30-49 | Cảnh báo nhẹ |
| High | 50-74 | Cọc ×1.5 |
| Critical | 75-100 | Cọc ×2, hạn chế tạo lobby |

### 10.3 RiskMultiplier
```
riskMultiplier = 1.0 + (riskScore / 100) × 1.0
```

## 11. Private Lobby & Invite

### 11.1 Visibility
- **Public**: xuất hiện trong search
- **Private**: chỉ join qua invite/share code

### 11.2 Share Code
- Sinh **6 ký tự** alphanumeric uppercase
- Unique trong hệ thống

### 11.3 Invite Rules
- Mỗi `(LobbyId, InviteeId)` chỉ có **1 invite Pending**
- Inviter phải là thành viên active
- Invitee không được là member active
- Private lobby: inviter phải là bạn bè của invitee
- Invite hết hạn sau **24 giờ**

## 12. Time Slot

### 12.1 Khung giờ cố định
| TimeSlot | Giờ |
|---|---|
| Morning | 06:00 - 12:00 |
| Afternoon | 12:00 - 17:00 |
| Evening | 17:00 - 23:00 |
| LateNight | 23:00 - 06:00 (qua đêm) |

### 12.2 Preferred Time
- `preferredStartTime`: optional, nằm trong [timeSlot.startTime, timeSlot.endTime]
- `preferredEndTime`: optional, nằm trong [preferredStartTime, timeSlot.endTime]

## 13. Cafe Duyệt Lobby (BR-NEW-11)

### Điều kiện
- Lobby **public** có `playDate > 2 ngày`
- Private lobby bỏ qua cafe duyệt

### Thời hạn
- **24 giờ** để cafe duyệt

### Kết quả
| Kết quả | Hành vi |
|---|---|
| Duyệt | Lobby → `Open` |
| Từ chối | Lobby → `RejectedByCafe`, hoàn 100% BVC |
| Không phản hồi | Lobby → `ExpiredByCafe`, hoàn 100% BVC |

## 14. Notification

### 4 mốc thông báo
| Mốc | Người nhận |
|---|---|
| 48h trước deadline | Host |
| 24h trước deadline | Host + Members |
| 2h trước giờ chơi | Host + Members |
| 30 phút trước giờ chơi | Host + Members |

## 15. Entity Relationships

```
Reservation (BR mới)
  ├── Host trả cọc BVC
  ├── Giữ ghế (SeatInventory)
  ├── Giữ game copy (GameInventory)
  ├── Link → Lobby
  └── Link → ActiveSession (khi check-in)

Booking (BR cũ - legacy)
  ├── Member trả cọc SePay
  └── Link → BookingDeposit
```

## 16. Ledger Entry Types

| Type | Tác động |
|---|---|
| `TOP_UP` | availableBalance += amount |
| `DEPOSIT_HOLD` | availableBalance -= amount; heldBalance += amount |
| `DEPOSIT_RELEASE` | heldBalance -= amount; availableBalance += amount |
| `DEPOSIT_CAPTURE` | heldBalance -= amount; settlement += amount (cho quán) |
| `DEPOSIT_FORFEIT` | heldBalance -= amount; forfeit += amount (no-show) |

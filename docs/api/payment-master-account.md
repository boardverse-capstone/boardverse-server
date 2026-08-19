# ⚠️ DEPRECATED: PaymentMasterAccountController

**This controller has been deprecated and consolidated into `SePayAccount`.**

**Use:** [`sepay-account.md`](./sepay-account.md) for all payment account management.

---

## Migration Guide

All functionality has been moved to `SePayAccount` with `AccountType = Master`:

| Old (PaymentMasterAccount) | New (SePayAccount) |
|---------------------------|---------------------|
| `POST /api/admin/payment-master-accounts` | `POST /api/admin/sepay-accounts` with `accountType: "Master"` |
| `GET /api/admin/payment-master-accounts` | `GET /api/admin/sepay-accounts?accountType=Master` |
| `GET /api/admin/payment-master-accounts/{id}` | `GET /api/admin/sepay-accounts/{id}` |
| `PUT /api/admin/payment-master-accounts/{id}` | `PUT /api/admin/sepay-accounts/{id}` |

### Example: Create Master Account (New API)

```http
POST /api/admin/sepay-accounts
Content-Type: application/json

{
  "accountType": "Master",
  "bankCode": "TPBANK",
  "accountNumber": "1234567890",
  "accountHolder": "BoardVerse Master Account"
}
```

### Benefits of Consolidation

1. **Single source of truth** - One table (`SePayAccounts`) for all payment accounts
2. **Environment switching** - Master accounts can use sandbox/production
3. **Consistent API** - Same patterns for master and cafe accounts
4. **Reduced complexity** - No duplicate entities or repositories

namespace BoardVerse.Tests;

/// <summary>
/// Provides unique test data generation to prevent conflicts when tests run multiple times.
/// Follows the rule: "Every test run must produce unique data."
/// </summary>
public static class TestDataFactory
{
    private static int _counter;
    private static readonly object _lock = new();

    /// <summary>
    /// Gets a monotonically increasing integer unique across all tests.
    /// Use this for unique order numbers, reference codes, etc.
    /// </summary>
    public static int GetNextId() => GetNextIdInternal();

    /// <summary>
    /// Gets a unique GUID for each call.
    /// </summary>
    public static Guid NewId() => Guid.NewGuid();

    /// <summary>
    /// Gets a unique order ID string for payment tests.
    /// </summary>
    public static string NewOrderId() => $"BV-TEST-{GetNextId():D6}";

    /// <summary>
    /// Gets a unique order ID string with custom prefix.
    /// </summary>
    public static string NewOrderId(string prefix) => $"{prefix}-TEST-{GetNextId():D6}";

    /// <summary>
    /// Gets a unique email address for test users.
    /// </summary>
    public static string NewEmail(string baseName = "test") => $"{baseName}{GetNextId()}@boardverse.test";

    /// <summary>
    /// Gets a unique barcode for POS inventory boxes.
    /// </summary>
    public static string NewBarcode() => $"BV-TEST-{GetNextId():D6}";

    /// <summary>
    /// Gets a unique barcode with cafe and inventory prefix.
    /// </summary>
    public static string NewBarcode(Guid cafeId, Guid inventoryId) =>
        $"BV-{cafeId.ToString("N")[..8]}-{inventoryId.ToString("N")[..8]}-{GetNextId():D3}";

    /// <summary>
    /// Gets a unique payment reference code.
    /// </summary>
    public static string NewPaymentRef() => $"REF{Guid.NewGuid():N[..12].ToUpperInvariant()}";

    /// <summary>
    /// Gets a unique transfer content for SePay webhooks.
    /// </summary>
    public static string NewTransferContent() => $"BV{Guid.NewGuid():N[..10].ToUpperInvariant()}";

    /// <summary>
    /// Resets the counter. Call this in test setup/teardown if needed.
    /// </summary>
    public static void Reset()
    {
        lock (_lock)
        {
            _counter = 0;
        }
    }

    private static int GetNextIdInternal()
    {
        lock (_lock)
        {
            return ++_counter;
        }
    }
}

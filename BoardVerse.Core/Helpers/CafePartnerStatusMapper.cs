namespace BoardVerse.Core.Helpers
{
    public static class CafePartnerStatusMapper
    {
        public static string ToApiApplicationStatus(Enum.CafePartnerApplicationStatus status) => status switch
        {
            Enum.CafePartnerApplicationStatus.PendingApproval => "PENDING_APPROVAL",
            Enum.CafePartnerApplicationStatus.Rejected => "REJECTED",
            Enum.CafePartnerApplicationStatus.Approved => "APPROVED",
            _ => status.ToString()
        };

        public static string ToApiOperationalStatus(Enum.CafePartnerOperationalStatus status) => status switch
        {
            Enum.CafePartnerOperationalStatus.DataBlank => "DATA_BLANK",
            Enum.CafePartnerOperationalStatus.Active => "ACTIVE",
            Enum.CafePartnerOperationalStatus.Inactive => "INACTIVE",
            Enum.CafePartnerOperationalStatus.Banned => "BANNED",
            _ => status.ToString()
        };

        public static bool TryParseApiOperationalStatus(string? value, out Enum.CafePartnerOperationalStatus status)
        {
            status = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.Trim().ToUpperInvariant() switch
            {
                "DATA_BLANK" => Assign(Enum.CafePartnerOperationalStatus.DataBlank, out status),
                "ACTIVE" => Assign(Enum.CafePartnerOperationalStatus.Active, out status),
                "INACTIVE" => Assign(Enum.CafePartnerOperationalStatus.Inactive, out status),
                "BANNED" => Assign(Enum.CafePartnerOperationalStatus.Banned, out status),
                _ => false
            };
        }

        private static bool Assign(Enum.CafePartnerOperationalStatus value, out Enum.CafePartnerOperationalStatus status)
        {
            status = value;
            return true;
        }

        public static string ToApiBillingModel(Enum.CafePartnerBillingModel model) => model switch
        {
            Enum.CafePartnerBillingModel.ByHour => "BY_HOUR",
            Enum.CafePartnerBillingModel.PerDrink => "PER_DRINK",
            _ => model.ToString()
        };
    }
}

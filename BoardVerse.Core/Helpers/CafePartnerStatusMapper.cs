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
            _ => status.ToString()
        };

        public static string ToApiBillingModel(Enum.CafePartnerBillingModel model) => model switch
        {
            Enum.CafePartnerBillingModel.ByHour => "BY_HOUR",
            Enum.CafePartnerBillingModel.PerDrink => "PER_DRINK",
            _ => model.ToString()
        };
    }
}

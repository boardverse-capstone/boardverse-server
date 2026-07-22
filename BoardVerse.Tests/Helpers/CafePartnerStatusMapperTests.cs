using BoardVerse.Core.Helpers;

namespace BoardVerse.Tests.Helpers;

public class CafePartnerStatusMapperTests
{
    [Theory]
    [InlineData(CafePartnerApplicationStatus.PendingApproval, "PENDING_APPROVAL")]
    [InlineData(CafePartnerApplicationStatus.Rejected, "REJECTED")]
    [InlineData(CafePartnerApplicationStatus.Approved, "APPROVED")]
    public void ToApiApplicationStatus_MapsKnown(CafePartnerApplicationStatus input, string expected)
    {
        Assert.Equal(expected, CafePartnerStatusMapper.ToApiApplicationStatus(input));
    }

    [Theory]
    [InlineData(CafePartnerOperationalStatus.DataBlank, "DATA_BLANK")]
    [InlineData(CafePartnerOperationalStatus.Active, "ACTIVE")]
    [InlineData(CafePartnerOperationalStatus.Inactive, "INACTIVE")]
    [InlineData(CafePartnerOperationalStatus.Banned, "BANNED")]
    public void ToApiOperationalStatus_MapsKnown(CafePartnerOperationalStatus input, string expected)
    {
        Assert.Equal(expected, CafePartnerStatusMapper.ToApiOperationalStatus(input));
    }

    [Theory]
    [InlineData("ACTIVE", CafePartnerOperationalStatus.Active, true)]
    [InlineData("active", CafePartnerOperationalStatus.Active, true)]
    [InlineData("BANNED", CafePartnerOperationalStatus.Banned, true)]
    [InlineData("INACTIVE", CafePartnerOperationalStatus.Inactive, true)]
    [InlineData("DATA_BLANK", CafePartnerOperationalStatus.DataBlank, true)]
    [InlineData("invalid", CafePartnerOperationalStatus.Active, false)]
    [InlineData("", CafePartnerOperationalStatus.Active, false)]
    [InlineData("   ", CafePartnerOperationalStatus.Active, false)]
    [InlineData(null, CafePartnerOperationalStatus.Active, false)]
    public void TryParseApiOperationalStatus(string? input, CafePartnerOperationalStatus expected, bool ok)
    {
        var result = CafePartnerStatusMapper.TryParseApiOperationalStatus(input, out var status);

        Assert.Equal(ok, result);
        if (ok) Assert.Equal(expected, status);
    }

    [Theory]
    [InlineData(CafePartnerBillingModel.TimeBased, "TIME_BASED")]
    [InlineData(CafePartnerBillingModel.FlatEntry, "FLAT_ENTRY")]
    public void ToApiBillingModel_MapsKnown(CafePartnerBillingModel input, string expected)
    {
        Assert.Equal(expected, CafePartnerStatusMapper.ToApiBillingModel(input));
    }
}
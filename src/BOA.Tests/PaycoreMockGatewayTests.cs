using BOA.Common.Contracts.Enums;
using BOA.Services.Card.Paycore;

namespace BOA.Tests;

/// <summary>
/// CardServiceTestHarness diğer testlerde FakePaycoreGateway kullanır (hızlı, deterministik).
/// Bu dosya ise gerçek uygulamada (BOA.App) DI'a kaydedilen PaycoreMockGateway'in kendisini —
/// kendi bağımsız SQLite dosyasını, referans üretimini ve onay/red mekaniğini — doğrudan test eder.
/// </summary>
public class PaycoreMockGatewayTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"paycore_test_{Guid.NewGuid():N}.db");
    private readonly PaycoreMockGateway _gateway;

    public PaycoreMockGatewayTests()
    {
        _gateway = new PaycoreMockGateway($"Data Source={_dbPath}");
    }

    [Fact]
    public void IssueCard_ReturnsUniqueReference()
    {
        var result = _gateway.IssueCard("435520******1234", "TEST USER", CardType.Credit);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("PCORE-CARD-", result.PaycoreCardReference);
    }

    [Fact]
    public void Authorize_ByDefault_Approves()
    {
        var card = _gateway.IssueCard("435520******1234", "TEST USER", CardType.Credit);

        var auth = _gateway.Authorize(card.PaycoreCardReference!, 250m, "BANKREF1");

        Assert.True(auth.IsApproved);
        Assert.StartsWith("PCORE-AUTH-", auth.PaycoreAuthReference);
        Assert.Equal("00", auth.ResponseCode);
    }

    [Fact]
    public void Authorize_WhenForcedToDecline_ReturnsDeclinedAndResetsFlag()
    {
        var card = _gateway.IssueCard("435520******1234", "TEST USER", CardType.Credit);
        _gateway.ForceDeclineNextAuthorization = true;

        var declined = _gateway.Authorize(card.PaycoreCardReference!, 250m, "BANKREF1");
        var approvedAfter = _gateway.Authorize(card.PaycoreCardReference!, 250m, "BANKREF2");

        Assert.False(declined.IsApproved);
        Assert.Equal("05", declined.ResponseCode);
        Assert.True(approvedAfter.IsApproved); // Bayrak tek seferlik — ikinci çağrı normale dönmeli
    }

    [Fact]
    public void Capture_UnknownReference_ReturnsFailureWithoutThrowing()
    {
        var result = _gateway.Capture("PCORE-AUTH-DOES-NOT-EXIST");

        Assert.False(result.IsSuccess);
    }

    public void Dispose()
    {
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* geçici dosya kilidi testin sonucunu etkilemez */ }
    }
}

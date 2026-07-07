using BOA.Common.Contracts.Requests;
using BOA.Tests.Infrastructure;
using CoreWCF;
using BOA.Common.Contracts.Base;

namespace BOA.Tests;

/// <summary>
/// CardService.CheckRole'ün, REST/JSON çağrılarında (OperationContext olmadığında) sessizce
/// atlanmadığını ve doğru rol eşlemesini (BranchTeller vs CardOperationsAdmin) uyguladığını doğrular.
/// </summary>
public class RbacTests
{
    [Fact]
    public void GetCardList_WithoutSecurityToken_ThrowsAuthenticationFault()
    {
        using var harness = new CardServiceTestHarness();
        harness.ClearRole();

        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" }));

        Assert.Equal("AUTHENTICATION_FAILED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void UpdateCardLimit_WithTellerToken_ThrowsAccessDenied()
    {
        using var harness = new CardServiceTestHarness();
        harness.SetRole(TestRole.Teller);

        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "RBAC TEST",
            NationalId = "77777777777",
            CardType = BOA.Common.Contracts.Enums.CardType.Debit,
            Limit = 0,
            InitialBalance = 1000,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        // UpdateCardLimit "CardOperationsAdmin" ister; Teller rolüyle reddedilmelidir.
        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = card.CreatedCard.CardId,
            NewLimit = 5000,
            Reason = "RBAC testi",
            Channel = "TEST",
            UserId = "test"
        }));

        Assert.Equal("ACCESS_DENIED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void UpdateCardLimit_WithAdminToken_Succeeds()
    {
        using var harness = new CardServiceTestHarness();
        harness.SetRole(TestRole.Teller);

        // Kredi kartı kullanılıyor: Debit kartlarda kredi limiti tanımlanamaz (bkz.
        // UpdateCardLimit_OnDebitCard_ThrowsValidationFault), bu yüzden RBAC-başarı senaryosu
        // için limit atanabilen bir kart tipi (Credit) gerekir.
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "RBAC TEST",
            NationalId = "88888888888",
            CardType = BOA.Common.Contracts.Enums.CardType.Credit,
            Limit = 1000,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        harness.SetRole(TestRole.Admin);
        // Limit artışı maker-checker'a girer; servis çağrısı başarılı olur, PendingRequest döner.
        var response = harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = card.CreatedCard.CardId,
            NewLimit = 5000,
            Reason = "RBAC testi — limit artış talebi",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.PendingRequest);
    }

    [Fact]
    public void UpdateCardLimit_OnDebitCard_ThrowsValidationFault()
    {
        // Debit kartlarda kredi limiti kavramı yoktur; UpdateCardLimit CreateCard'daki aynı kısıtı
        // uygulamıyorsa bir admin, kart tipini kontrol etmeden bir Debit karta limit tanımlayabilirdi.
        using var harness = new CardServiceTestHarness();
        harness.SetRole(TestRole.Teller);

        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "DEBIT LIMIT TEST",
            NationalId = "99999999999",
            CardType = BOA.Common.Contracts.Enums.CardType.Debit,
            Limit = 0,
            InitialBalance = 1000,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        harness.SetRole(TestRole.Admin);
        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = card.CreatedCard.CardId,
            NewLimit = 5000,
            Reason = "Debit limit testi",
            Channel = "TEST",
            UserId = "test"
        }));

        Assert.Equal("VALIDATION_ERROR", fault.Detail.ErrorCode);
    }

    [Fact]
    public void RunEodBatch_WithTellerToken_ThrowsAccessDenied()
    {
        using var harness = new CardServiceTestHarness();
        harness.SetRole(TestRole.Teller);

        Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" }));
    }
}

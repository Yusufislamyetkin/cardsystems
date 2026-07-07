using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Tests.Infrastructure;
using CoreWCF;
using BOA.Common.Contracts.Base;

namespace BOA.Tests;

/// <summary>
/// Kredi kartı limit artışının maker-checker (çift onay / four-eyes) akışını doğrular.
/// Limit artışı tek kişinin girip uygulayabileceği bir işlem değildir: bir kullanıcı (maker)
/// talebi girer, FARKLI bir yetkili (checker) onaylar veya reddeder.
/// Limit düşüşleri risk azalttığı için anında uygulanır (maker-checker'a girmez).
/// </summary>
public class MakerCheckerLimitTests
{
    private static (CardServiceTestHarness harness, int cardId) CreateCreditCard(decimal limit)
    {
        var harness = new CardServiceTestHarness();
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "MAKER CHECKER TEST",
            NationalId = "66666666666",
            CardType = CardType.Credit,
            Limit = limit,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        return (harness, card.CreatedCard.CardId);
    }

    [Fact]
    public void LimitIncrease_CreatesPendingRequest_DoesNotChangeLimit()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        var response = harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = cardId,
            NewLimit = 10000,
            Reason = "Müşteri gelir belgesi güncellendi",
            Channel = "TEST",
            UserId = "maker_user"
        });

        Assert.True(response.IsSuccess);
        Assert.NotNull(response.PendingRequest);
        Assert.Equal(LimitChangeRequestStatus.PendingApproval, response.PendingRequest!.Status);
        Assert.Equal(5000, response.PendingRequest.CurrentLimit);
        Assert.Equal(10000, response.PendingRequest.RequestedLimit);
        Assert.Equal("maker_user", response.PendingRequest.MakerUserId);
        Assert.Equal(5000, response.UpdatedCard.CardLimit); // Limit henüz değişmedi
    }

    [Fact]
    public void LimitDecrease_AppliesImmediately_NoMakerChecker()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        var response = harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = cardId,
            NewLimit = 3000,
            Reason = "Risk puanı düşüşü",
            Channel = "TEST",
            UserId = "admin_user"
        });

        Assert.True(response.IsSuccess);
        Assert.Null(response.PendingRequest); // Düşüş, maker-checker'a girmez
        Assert.Equal(3000, response.UpdatedCard.CardLimit); // Anında uygulandı
    }

    [Fact]
    public void LimitDecrease_BelowDebt_SetsOverlimitFlag()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;

        // 3000 TL harcama yap (bakiye: -3000)
        harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 3000,
            Description = "Harcama",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        harness.SetRole(TestRole.Admin);
        var response = harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = cardId,
            NewLimit = 2000, // Borç 3000 > yeni limit 2000
            Reason = "Limit kısıtlama",
            Channel = "TEST",
            UserId = "admin_user"
        });

        Assert.True(response.IsSuccess);
        Assert.True(response.IsOverlimit);
        Assert.Equal(2000, response.UpdatedCard.CardLimit);
    }

    [Fact]
    public void DecideLimitChange_Approve_AppliesNewLimit()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        // Maker: talep oluştur
        var createResp = harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = cardId,
            NewLimit = 10000,
            Reason = "Müşteri talebi",
            Channel = "TEST",
            UserId = "maker_user"
        });
        int reqId = createResp.PendingRequest!.LimitRequestId;

        // Checker (farklı kullanıcı): onayla
        var decideResp = harness.Service.DecideCardLimitChange(new DecideCardLimitChangeRequest
        {
            LimitRequestId = reqId,
            Approve = true,
            DecisionNote = "Gelir belgesi uygun",
            Channel = "TEST",
            UserId = "checker_user"
        });

        Assert.True(decideResp.IsSuccess);
        Assert.Equal(LimitChangeRequestStatus.Approved, decideResp.DecidedRequest!.Status);
        Assert.Equal("checker_user", decideResp.DecidedRequest.CheckerUserId);
        Assert.Equal(10000, decideResp.UpdatedCard!.CardLimit); // Limit artık uygulandı
    }

    [Fact]
    public void DecideLimitChange_Reject_DoesNotChangeLimit()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        var createResp = harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = cardId,
            NewLimit = 10000,
            Reason = "Müşteri talebi",
            Channel = "TEST",
            UserId = "maker_user"
        });
        int reqId = createResp.PendingRequest!.LimitRequestId;

        var decideResp = harness.Service.DecideCardLimitChange(new DecideCardLimitChangeRequest
        {
            LimitRequestId = reqId,
            Approve = false,
            DecisionNote = "Gelir belgesi yetersiz",
            Channel = "TEST",
            UserId = "checker_user"
        });

        Assert.True(decideResp.IsSuccess);
        Assert.Equal(LimitChangeRequestStatus.Rejected, decideResp.DecidedRequest!.Status);

        // Kartın limiti değişmemiş olmalı
        harness.SetRole(TestRole.Teller);
        var card = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" })
            .Cards.Single(c => c.CardId == cardId);
        Assert.Equal(5000, card.CardLimit);
    }

    [Fact]
    public void DecideLimitChange_SameUserAsMaker_ThrowsFourEyesViolation()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        var createResp = harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = cardId,
            NewLimit = 10000,
            Reason = "Müşteri talebi",
            Channel = "TEST",
            UserId = "same_user"
        });
        int reqId = createResp.PendingRequest!.LimitRequestId;

        // Aynı kullanıcı kendi talebini onaylamaya çalışıyor
        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.DecideCardLimitChange(
            new DecideCardLimitChangeRequest
            {
                LimitRequestId = reqId,
                Approve = true,
                Channel = "TEST",
                UserId = "same_user"
            }));

        Assert.Equal("FOUR_EYES_VIOLATION", fault.Detail.ErrorCode);
    }

    [Fact]
    public void UpdateCardLimit_WithoutReason_ThrowsValidationError()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.UpdateCardLimit(
            new UpdateCardLimitRequest
            {
                CardId = cardId,
                NewLimit = 3000,
                Reason = "",
                Channel = "TEST",
                UserId = "admin"
            }));

        Assert.Equal("VALIDATION_ERROR", fault.Detail.ErrorCode);
    }

    [Fact]
    public void UpdateCardLimit_OnCancelledCard_ThrowsError()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        harness.Service.SetCardStatus(new SetCardStatusRequest
        {
            CardId = cardId,
            NewStatus = CardStatus.Cancelled,
            Reason = "Test iptali",
            Channel = "TEST",
            UserId = "admin"
        });

        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.UpdateCardLimit(
            new UpdateCardLimitRequest
            {
                CardId = cardId,
                NewLimit = 3000,
                Reason = "İptal edilen karta limit",
                Channel = "TEST",
                UserId = "admin"
            }));

        Assert.Equal("CARD_CANCELLED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void UpdateCardLimit_OnBlockedCard_ThrowsError()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        harness.Service.SetCardStatus(new SetCardStatusRequest
        {
            CardId = cardId,
            NewStatus = CardStatus.Blocked,
            Reason = "Test bloke",
            Channel = "TEST",
            UserId = "admin"
        });

        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.UpdateCardLimit(
            new UpdateCardLimitRequest
            {
                CardId = cardId,
                NewLimit = 3000,
                Reason = "Bloke karta limit",
                Channel = "TEST",
                UserId = "admin"
            }));

        Assert.Equal("CARD_BLOCKED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void GetLimitChangeRequests_ListsPendingRequests()
    {
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        harness.Service.UpdateCardLimit(new UpdateCardLimitRequest
        {
            CardId = cardId,
            NewLimit = 8000,
            Reason = "Talep 1",
            Channel = "TEST",
            UserId = "maker1"
        });

        var listResp = harness.Service.GetLimitChangeRequests(new GetLimitChangeRequestsRequest
        {
            OnlyPending = true,
            Channel = "TEST",
            UserId = "admin"
        });

        Assert.True(listResp.IsSuccess);
        Assert.Single(listResp.Requests);
        Assert.Equal(LimitChangeRequestStatus.PendingApproval, listResp.Requests[0].Status);
    }

    [Fact]
    public void DirectTransaction_WithHeldAuthorization_ReducesAvailableLimit()
    {
        // Fix #1 regresyon testi: direkt harcamada blokeler düşülüyor mu?
        var (harness, cardId) = CreateCreditCard(5000);
        using var _ = harness;

        // 4000 TL provizyon al (hold)
        var auth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 4000,
            Description = "Hold",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        Assert.Equal(AuthResponseCode.Approved, auth.Authorization!.ResponseCode);

        // Kalan kullanılabilir: 5000 - 4000 (hold) = 1000. 1500 TL'lik harcama reddedilmeli.
        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateTransaction(
            new CreateTransactionRequest
            {
                CardId = cardId,
                TransactionType = TransactionType.Purchase,
                Amount = 1500,
                Description = "Hold sonrası harcama",
                Pin = "1234",
                Channel = "TEST",
                UserId = "test"
            }));

        Assert.Contains("Yetersiz", fault.Detail.ErrorMessage);
    }
}

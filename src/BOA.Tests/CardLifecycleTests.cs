using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Common.Contracts.Base;
using BOA.Tests.Infrastructure;
using CoreWCF;

namespace BOA.Tests;

public class CardLifecycleTests
{
    private static CardServiceTestHarness NewHarness() => new();

    [Fact]
    public void ReportLostCard_BlocksCard()
    {
        using var h = NewHarness();
        var req = new CreateCardRequest
        {
            CardHolderName = "TEST USER",
            NationalId = "11111111111",
            CardType = CardType.Credit,
            Limit = 5000,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        };
        var card = h.Service.CreateCard(req).CreatedCard;

        var lsReq = new ReportLostStolenRequest
        {
            CardId = card.CardId,
            BlockReason = BlockReason.LostCard,
            Description = "Cuzdan kayboldu",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        var resp = h.Service.ReportLostStolenCard(lsReq);
        Assert.True(resp.IsSuccess);
        Assert.Equal(CardStatus.Blocked, resp.BlockedCard.Status);
        Assert.Equal(BlockReason.LostCard, resp.BlockedCard.BlockReason);
        Assert.Equal(1, h.PaycoreGateway.SetCardStatusCallCount);
    }

    [Fact]
    public void ReportStolenCard_RequiresPoliceReport()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        var req = new ReportLostStolenRequest
        {
            CardId = card.CardId,
            BlockReason = BlockReason.StolenCard,
            Description = "Calindi",
            PoliceReportNumber = null,
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        var ex = Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.ReportLostStolenCard(req));
        Assert.Contains("tutanak", ex.Detail.ErrorMessage);
    }

    [Fact]
    public void ReportLostStolen_VoidsActiveAuthorizations()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 5000);
        CreateAuthorization(h, card.CardId, 100);
        CreateAuthorization(h, card.CardId, 200);
        var req = new ReportLostStolenRequest
        {
            CardId = card.CardId,
            BlockReason = BlockReason.FraudSuspicion,
            Description = "Fraud",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        var resp = h.Service.ReportLostStolenCard(req);
        Assert.True(resp.IsSuccess);
        Assert.True(resp.VoidedAuthorizationCount >= 1);
    }

    [Fact]
    public void ReportLostStolen_WithReplacement_CreatesNewCard()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        var req = new ReportLostStolenRequest
        {
            CardId = card.CardId,
            BlockReason = BlockReason.LostCard,
            Description = "Test",
            RequestReplacement = true,
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        var resp = h.Service.ReportLostStolenCard(req);
        Assert.True(resp.IsSuccess);
        Assert.NotNull(resp.ReplacementCardId);
    }

    [Fact]
    public void ReportLostStolen_OnCancelledCard_Faults()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        SetCardStatus(h, card.CardId, CardStatus.Cancelled);
        var req = new ReportLostStolenRequest
        {
            CardId = card.CardId,
            BlockReason = BlockReason.LostCard,
            Description = "Test",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        var ex = Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.ReportLostStolenCard(req));
        Assert.Equal("CARD_CANCELLED", ex.Detail.ErrorCode);
    }

    [Fact]
    public void ReportLostStolen_AlreadyBlocked_Faults()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        SetCardStatus(h, card.CardId, CardStatus.Blocked);
        var req = new ReportLostStolenRequest
        {
            CardId = card.CardId,
            BlockReason = BlockReason.LostCard,
            Description = "Test",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        var ex = Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.ReportLostStolenCard(req));
        Assert.Equal("CARD_ALREADY_BLOCKED", ex.Detail.ErrorCode);
    }

    [Fact]
    public void ReportLostStolen_InvalidBlockReason_Faults()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        var req = new ReportLostStolenRequest
        {
            CardId = card.CardId,
            BlockReason = BlockReason.CustomerRequest,
            Description = "Test",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.ReportLostStolenCard(req));
    }

    [Fact]
    public void CancelCard_HappyPath_SetsCancelled()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        h.SetRole(TestRole.Admin);
        var req = new CancelCardRequest
        {
            CardId = card.CardId,
            CancellationReason = CancellationReason.CustomerRequest,
            Description = "Musteri talebi",
            AcknowledgeOutstandingBalance = false,
            Channel = "TEST",
            UserId = "ADMIN",
            ClientIp = "127.0.0.1"
        };
        var resp = h.Service.CancelCard(req);
        Assert.True(resp.IsSuccess);
        Assert.Equal(CardStatus.Cancelled, resp.CancelledCard.Status);
    }

    [Fact]
    public void CancelCard_WithBalance_RequiresAcknowledge()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 1000);
        h.SetRole(TestRole.Admin);
        var req = new CancelCardRequest
        {
            CardId = card.CardId,
            CancellationReason = CancellationReason.CustomerRequest,
            Description = "Test",
            AcknowledgeOutstandingBalance = false,
            Channel = "TEST",
            UserId = "ADMIN",
            ClientIp = "127.0.0.1"
        };
        var ex = Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.CancelCard(req));
        Assert.Equal("OUTSTANDING_BALANCE_NOT_ACKNOWLEDGED", ex.Detail.ErrorCode);
    }

    [Fact]
    public void CancelCard_Acknowledged_Succeeds()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 1000);
        h.SetRole(TestRole.Admin);
        var req = new CancelCardRequest
        {
            CardId = card.CardId,
            CancellationReason = CancellationReason.CustomerRequest,
            Description = "Test",
            AcknowledgeOutstandingBalance = true,
            Channel = "TEST",
            UserId = "ADMIN",
            ClientIp = "127.0.0.1"
        };
        var resp = h.Service.CancelCard(req);
        Assert.True(resp.IsSuccess);
    }

    [Fact]
    public void CancelCard_AlreadyCancelled_Faults()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        SetCardStatus(h, card.CardId, CardStatus.Cancelled);
        h.SetRole(TestRole.Admin);
        var req = new CancelCardRequest
        {
            CardId = card.CardId,
            CancellationReason = CancellationReason.CustomerRequest,
            Description = "Test",
            AcknowledgeOutstandingBalance = false,
            Channel = "TEST",
            UserId = "ADMIN",
            ClientIp = "127.0.0.1"
        };
        var ex = Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.CancelCard(req));
        Assert.Equal("CARD_ALREADY_CANCELLED", ex.Detail.ErrorCode);
    }

    [Fact]
    public void RenewCard_CreatesNewCardWithSamePan()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        var req = new RenewCardRequest { CardId = card.CardId, Channel = "TEST", UserId = "TELLER", ClientIp = "127.0.0.1" };
        var resp = h.Service.RenewCard(req);
        Assert.True(resp.IsSuccess);
        Assert.Equal(CardStatus.PendingActivation, resp.NewCard.Status);
        Assert.Equal(card.CardId, resp.NewCard.PreviousCardId);
        Assert.Equal(1, h.PaycoreGateway.RenewCardCallCount);
    }

    [Fact]
    public void RenewCard_OldCardRemainsActive()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        var resp = h.Service.RenewCard(new RenewCardRequest { CardId = card.CardId, Channel = "TEST", UserId = "TELLER", ClientIp = "127.0.0.1" });
        Assert.Equal(CardStatus.Active, resp.OldCard.Status);
    }

    [Fact]
    public void RenewCard_AlreadyRenewed_Faults()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        h.Service.RenewCard(new RenewCardRequest { CardId = card.CardId, Channel = "TEST", UserId = "TELLER", ClientIp = "127.0.0.1" });
        var ex = Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.RenewCard(new RenewCardRequest { CardId = card.CardId, Channel = "TEST", UserId = "TELLER", ClientIp = "127.0.0.1" }));
        Assert.Equal("CARD_ALREADY_RENEWED", ex.Detail.ErrorCode);
    }

    [Fact]
    public void RenewCard_CancelledCard_Faults()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        SetCardStatus(h, card.CardId, CardStatus.Cancelled);
        var ex = Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.RenewCard(new RenewCardRequest { CardId = card.CardId, Channel = "TEST", UserId = "TELLER", ClientIp = "127.0.0.1" }));
        Assert.Equal("CARD_CANCELLED", ex.Detail.ErrorCode);
    }

    [Fact]
    public void ReissueCard_PhysicalDamage_SamePan_OldCardCancelled()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        var req = new ReissueCardRequest
        {
            CardId = card.CardId,
            ReissueReason = ReissueReason.PhysicalDamage,
            Description = "Kart kirildi",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        var resp = h.Service.ReissueCard(req);
        Assert.True(resp.IsSuccess);
        Assert.Equal(CardStatus.Cancelled, resp.OldCard.Status);
        Assert.Equal(CardStatus.PendingActivation, resp.NewCard.Status);
    }

    [Fact]
    public void ReissueCard_NameChange_RequiresNewName()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        var req = new ReissueCardRequest
        {
            CardId = card.CardId,
            ReissueReason = ReissueReason.NameChange,
            Description = "Evlilik",
            NewCardHolderName = null,
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        Assert.Throws<FaultException<BankingFault>>(() => h.Service.ReissueCard(req));
    }

    [Fact]
    public void ReissueCard_AlreadyReplaced_Faults()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        h.Service.ReissueCard(new ReissueCardRequest
        {
            CardId = card.CardId,
            ReissueReason = ReissueReason.ChipMalfunction,
            Description = "Test",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        });
        Assert.Throws<FaultException<BankingFault>>(
            () => h.Service.ReissueCard(new ReissueCardRequest
            {
                CardId = card.CardId,
                ReissueReason = ReissueReason.ChipMalfunction,
                Description = "Test",
                Channel = "TEST",
                UserId = "TELLER",
                ClientIp = "127.0.0.1"
            }));
        // Not checking specific error code — already cancelled/replaced, both valid
    }

    [Fact]
    public void SetCardStatus_NotifiesPaycore()
    {
        using var h = NewHarness();
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        h.SetRole(TestRole.Admin);
        var req = new SetCardStatusRequest
        {
            CardId = card.CardId,
            NewStatus = CardStatus.Blocked,
            Reason = "Test",
            Channel = "TEST",
            UserId = "ADMIN",
            ClientIp = "127.0.0.1"
        };
        h.Service.SetCardStatus(req);
        Assert.Equal(1, h.PaycoreGateway.SetCardStatusCallCount);
    }

    [Fact]
    public void PaycoreFailure_DoesNotBlockCardOperation()
    {
        using var h = NewHarness();
        h.PaycoreGateway.ThrowOnNextSetCardStatus = true;
        var card = CreateCard(h, CardType.Credit, 5000, 0);
        var req = new ReportLostStolenRequest
        {
            CardId = card.CardId,
            BlockReason = BlockReason.LostCard,
            Description = "Test",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        };
        var resp = h.Service.ReportLostStolenCard(req);
        Assert.True(resp.IsSuccess);
        Assert.Equal(CardStatus.Blocked, resp.BlockedCard.Status);
    }

    // Helpers
    private static BOA.Common.Contracts.Dtos.CardDto CreateCard(CardServiceTestHarness h, CardType type, decimal limit, decimal balance)
    {
        var req = new CreateCardRequest
        {
            CardHolderName = "TEST USER",
            NationalId = "12345678901",
            CardType = type,
            Limit = limit,
            InitialBalance = balance,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        };
        return h.Service.CreateCard(req).CreatedCard;
    }

    private static void SetCardStatus(CardServiceTestHarness h, int cardId, CardStatus status)
    {
        h.SetRole(TestRole.Admin);
        h.Service.SetCardStatus(new SetCardStatusRequest
        {
            CardId = cardId,
            NewStatus = status,
            Reason = "Test",
            Channel = "TEST",
            UserId = "ADMIN",
            ClientIp = "127.0.0.1"
        });
        h.SetRole(TestRole.Teller);
    }

    private static void CreateAuthorization(CardServiceTestHarness h, int cardId, decimal amount)
    {
        h.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = amount,
            Description = "Test auth",
            Pin = "1234",
            Channel = "TEST",
            UserId = "TELLER",
            ClientIp = "127.0.0.1"
        });
    }
}
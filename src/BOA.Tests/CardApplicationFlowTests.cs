using System.Data;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Tests.Infrastructure;
using CoreWCF;
using Microsoft.Data.Sqlite;

namespace BOA.Tests;

public class CardApplicationFlowTests
{
    private static ApplyForCreditCardResponse Apply(CardServiceTestHarness harness, string tckn, decimal income, decimal requested,
        string userId = "teller_maker", string name = "TEST APPLICANT")
    {
        return harness.Service.ApplyForCreditCard(new ApplyForCreditCardRequest
        {
            NationalId = tckn,
            ApplicantName = name,
            DeclaredMonthlyIncome = income,
            RequestedLimit = requested,
            Channel = "TEST",
            UserId = userId
        });
    }

    [Fact]
    public void Apply_HighScore_AutoApproved()
    {
        using var harness = new CardServiceTestHarness();
        var r = Apply(harness, "11111111111", 50000, 60000);
        Assert.True(r.IsSuccess);
        Assert.Equal(CardApplicationStatus.AutoApproved, r.Application!.Status);
        Assert.Equal(60000, r.Application.ApprovedLimit);
        Assert.Equal(1508, r.Application.CreditScore);
    }

    [Fact]
    public void Apply_LowScore_AutoRejected()
    {
        using var harness = new CardServiceTestHarness();
        var r = Apply(harness, "50000000000", 50000, 60000);
        Assert.Equal(CardApplicationStatus.AutoRejected, r.Application!.Status);
        Assert.Equal(686, r.Application.CreditScore);
    }

    [Fact]
    public void Apply_MidScore_ManualReview()
    {
        using var harness = new CardServiceTestHarness();
        var r = Apply(harness, "60000000000", 50000, 60000);
        Assert.Equal(CardApplicationStatus.ManualReview, r.Application!.Status);
        Assert.Equal(823, r.Application.CreditScore);
        Assert.Null(r.Application.ApprovedLimit);
    }

    [Fact]
    public void Apply_NewCustomer_LimitCappedAt2xIncome()
    {
        using var harness = new CardServiceTestHarness();
        var r = Apply(harness, "11111111111", 30000, 100000);
        Assert.Equal(CardApplicationStatus.AutoApproved, r.Application!.Status);
        Assert.Equal(60000, r.Application.ApprovedLimit);
        Assert.Contains("BDDK", r.Application.DecisionReason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Apply_ExistingCreditCardCustomer_4xCapMinusExposure()
    {
        using var harness = new CardServiceTestHarness();
        harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "EXISTING CUSTOMER",
            NationalId = "22222222222",
            CardType = CardType.Credit,
            Limit = 50000,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        var r = Apply(harness, "22222222222", 20000, 80000);
        Assert.Equal(CardApplicationStatus.AutoApproved, r.Application!.Status);
        Assert.Equal(30000, r.Application.ApprovedLimit);
    }

    [Fact]
    public void Apply_ExposureFillsCap_AutoRejected()
    {
        using var harness = new CardServiceTestHarness();
        harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "FULL EXPOSURE",
            NationalId = "33333333333",
            CardType = CardType.Credit,
            Limit = 80000,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        var r = Apply(harness, "33333333333", 20000, 10000);
        Assert.Equal(CardApplicationStatus.AutoRejected, r.Application!.Status);
    }

    [Fact]
    public void Apply_DuplicateOpenApplication_Faults()
    {
        using var harness = new CardServiceTestHarness();
        Apply(harness, "60000000000", 50000, 60000);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            Apply(harness, "60000000000", 50000, 60000));
        Assert.Equal("APPLICATION_ALREADY_PENDING", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Apply_InvalidNationalId_Or_ZeroIncome_Faults()
    {
        using var harness = new CardServiceTestHarness();
        Assert.Throws<FaultException<BankingFault>>(() => Apply(harness, "123", 50000, 60000));
        Assert.Throws<FaultException<BankingFault>>(() => Apply(harness, "11111111111", 0, 60000));
    }

    [Fact]
    public void Decide_SameUserAsMaker_FourEyesViolation()
    {
        using var harness = new CardServiceTestHarness();
        var app = Apply(harness, "60000000000", 50000, 60000, userId: "same_user").Application!;
        harness.SetRole(TestRole.Admin);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.DecideCardApplication(new DecideCardApplicationRequest
            {
                ApplicationId = app.ApplicationId,
                Approve = true,
                DecisionNote = "Onay",
                Channel = "TEST",
                UserId = "same_user"
            }));
        Assert.Equal("FOUR_EYES_VIOLATION", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Decide_ApproveAboveBddkCap_Faults()
    {
        using var harness = new CardServiceTestHarness();
        var app = Apply(harness, "60000000000", 30000, 50000, userId: "maker_a").Application!;
        harness.SetRole(TestRole.Admin);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.DecideCardApplication(new DecideCardApplicationRequest
            {
                ApplicationId = app.ApplicationId,
                Approve = true,
                ApprovedLimit = 999999,
                DecisionNote = "Fazla limit",
                Channel = "TEST",
                UserId = "checker_b"
            }));
        Assert.Equal("BDDK_LIMIT_EXCEEDED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Decide_Approve_DefaultsToMinOfRequestedAndCap()
    {
        using var harness = new CardServiceTestHarness();
        var app = Apply(harness, "60000000000", 30000, 100000, userId: "maker_a").Application!;
        harness.SetRole(TestRole.Admin);
        var r = harness.Service.DecideCardApplication(new DecideCardApplicationRequest
        {
            ApplicationId = app.ApplicationId,
            Approve = true,
            DecisionNote = "Onay",
            Channel = "TEST",
            UserId = "checker_b"
        });
        Assert.Equal(CardApplicationStatus.Approved, r.Application!.Status);
        Assert.Equal(60000, r.Application.ApprovedLimit);
    }

    [Fact]
    public void Decide_Reject_SetsRejected()
    {
        using var harness = new CardServiceTestHarness();
        var app = Apply(harness, "60000000000", 50000, 60000, userId: "maker_a").Application!;
        harness.SetRole(TestRole.Admin);
        var r = harness.Service.DecideCardApplication(new DecideCardApplicationRequest
        {
            ApplicationId = app.ApplicationId,
            Approve = false,
            DecisionNote = "Red",
            Channel = "TEST",
            UserId = "checker_b"
        });
        Assert.Equal(CardApplicationStatus.Rejected, r.Application!.Status);
    }

    [Fact]
    public void Decide_NonManualReviewApplication_Faults()
    {
        using var harness = new CardServiceTestHarness();
        var app = Apply(harness, "11111111111", 50000, 60000).Application!;
        harness.SetRole(TestRole.Admin);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.DecideCardApplication(new DecideCardApplicationRequest
            {
                ApplicationId = app.ApplicationId,
                Approve = true,
                DecisionNote = "Geçersiz",
                Channel = "TEST",
                UserId = "checker_b"
            }));
        Assert.Equal("APPLICATION_STATE_INVALID", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Apply_WithAdminToken_ThrowsAccessDenied()
    {
        using var harness = new CardServiceTestHarness();
        harness.SetRole(TestRole.Admin);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            Apply(harness, "11111111111", 50000, 60000));
        Assert.Equal("ACCESS_DENIED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Decide_WithTellerToken_ThrowsAccessDenied()
    {
        using var harness = new CardServiceTestHarness();
        var app = Apply(harness, "60000000000", 50000, 60000, userId: "maker_a").Application!;
        harness.SetRole(TestRole.Teller);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.DecideCardApplication(new DecideCardApplicationRequest
            {
                ApplicationId = app.ApplicationId,
                Approve = true,
                DecisionNote = "Onay",
                Channel = "TEST",
                UserId = "teller_x"
            }));
        Assert.Equal("ACCESS_DENIED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Eod_IssuesApprovedApplication_CardPendingActivation_NoPin()
    {
        using var harness = new CardServiceTestHarness();
        var app = Apply(harness, "11111111111", 50000, 60000).Application!;
        harness.SetRole(TestRole.Admin);
        var eod = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "admin" });
        Assert.Equal(1, eod.CardsIssued);

        harness.SetRole(TestRole.Teller);
        var list = harness.Service.GetCardApplications(new GetCardApplicationsRequest { Channel = "TEST", UserId = "t" });
        var issued = list.Applications.First(a => a.ApplicationId == app.ApplicationId);
        Assert.Equal(CardApplicationStatus.Issued, issued.Status);
        Assert.NotNull(issued.CardId);

        var cards = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST", UserId = "t" });
        var card = cards.Cards.First(c => c.CardId == issued.CardId);
        Assert.Equal(CardStatus.PendingActivation, card.Status);
        Assert.True(harness.PaycoreGateway.IssueCardCallCount >= 1);

        var pinResult = harness.Service.VerifyPin(new VerifyPinRequest { CardId = card.CardId, Pin = "1234", Channel = "TEST", UserId = "t" });
        Assert.False(pinResult.IsPinValid);
    }

    [Fact]
    public void Eod_PaycoreThrowsAtIssuance_CardStillIssued()
    {
        using var harness = new CardServiceTestHarness();
        Apply(harness, "11111111111", 50000, 60000);
        harness.PaycoreGateway.ThrowOnNextIssueCard = true;
        harness.SetRole(TestRole.Admin);
        var eod = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "admin" });
        Assert.Equal(1, eod.CardsIssued);
    }

    [Fact]
    public void Eod_Rerun_DoesNotIssueTwice()
    {
        using var harness = new CardServiceTestHarness();
        Apply(harness, "11111111111", 50000, 60000);
        harness.SetRole(TestRole.Admin);
        harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "admin" });
        var eod2 = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "admin" });
        Assert.Equal(0, eod2.CardsIssued);
        harness.SetRole(TestRole.Teller);
        var cards = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST", UserId = "t" });
        Assert.Equal(1, cards.Cards.Count(c => c.NationalId == "11111111111"));
    }

    [Fact]
    public void Eod_ExpiresStaleManualReviewApplications()
    {
        using var harness = new CardServiceTestHarness();
        var app = Apply(harness, "60000000000", 50000, 60000).Application!;
        using (var conn = new SqliteConnection(harness.ConnectionString))
        {
            conn.Open();
            using var cmd = new SqliteCommand(
                "UPDATE boa_card_applications SET created_date = @d WHERE application_id = @id", conn);
            cmd.Parameters.AddWithValue("@d", DateTime.Now.AddDays(-31).ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@id", app.ApplicationId);
            cmd.ExecuteNonQuery();
        }
        harness.SetRole(TestRole.Admin);
        var eod = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "admin" });
        Assert.Equal(1, eod.ApplicationsExpired);
        harness.SetRole(TestRole.Teller);
        var list = harness.Service.GetCardApplications(new GetCardApplicationsRequest { Channel = "TEST", UserId = "t" });
        Assert.Equal(CardApplicationStatus.Expired, list.Applications.First(a => a.ApplicationId == app.ApplicationId).Status);
    }

    [Fact]
    public void Activate_HappyPath_ActiveAndPinWorks()
    {
        using var harness = new CardServiceTestHarness();
        Apply(harness, "11111111111", 50000, 60000);
        harness.SetRole(TestRole.Admin);
        harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "admin" });
        harness.SetRole(TestRole.Teller);
        var issued = harness.Service.GetCardApplications(new GetCardApplicationsRequest { Channel = "TEST", UserId = "t" })
            .Applications.First(a => a.NationalId == "11111111111");
        var act = harness.Service.ActivateCard(new ActivateCardRequest
        {
            CardId = issued.CardId!.Value,
            NationalId = "11111111111",
            Pin = "5678",
            Channel = "TEST",
            UserId = "teller"
        });
        Assert.Equal(CardStatus.Active, act.ActivatedCard!.Status);
        var verify = harness.Service.VerifyPin(new VerifyPinRequest { CardId = act.ActivatedCard.CardId, Pin = "5678", Channel = "TEST", UserId = "t" });
        Assert.True(verify.IsPinValid);
    }

    [Fact]
    public void Activate_WrongNationalId_Faults()
    {
        using var harness = new CardServiceTestHarness();
        var cardId = IssuePendingCard(harness);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.ActivateCard(new ActivateCardRequest
            {
                CardId = cardId,
                NationalId = "99999999999",
                Pin = "1234",
                Channel = "TEST",
                UserId = "t"
            }));
        Assert.Equal("IDENTITY_VERIFICATION_FAILED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Activate_AlreadyActive_Faults()
    {
        using var harness = new CardServiceTestHarness();
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "ACTIVE CARD",
            NationalId = "44444444444",
            CardType = CardType.Credit,
            Limit = 5000,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.ActivateCard(new ActivateCardRequest
            {
                CardId = card.CreatedCard.CardId,
                NationalId = "44444444444",
                Pin = "1234",
                Channel = "TEST",
                UserId = "t"
            }));
        Assert.Equal("CARD_STATE_INVALID", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Activate_InvalidPin_Faults()
    {
        using var harness = new CardServiceTestHarness();
        var cardId = IssuePendingCard(harness);
        Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.ActivateCard(new ActivateCardRequest
            {
                CardId = cardId,
                NationalId = "11111111111",
                Pin = "12",
                Channel = "TEST",
                UserId = "t"
            }));
    }

    [Fact]
    public void Transaction_OnPendingActivationCard_Rejected()
    {
        using var harness = new CardServiceTestHarness();
        var cardId = IssuePendingCard(harness);
        Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.CreateTransaction(new CreateTransactionRequest
            {
                CardId = cardId,
                TransactionType = TransactionType.Purchase,
                Amount = 100,
                Description = "Test",
                Pin = "1234",
                Channel = "TEST",
                UserId = "t"
            }));
    }

    [Fact]
    public void Authorize_OnPendingActivationCard_Rejected()
    {
        using var harness = new CardServiceTestHarness();
        var cardId = IssuePendingCard(harness);
        var auth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 100,
            Description = "Test",
            Pin = "1234",
            Channel = "TEST",
            UserId = "t"
        });
        Assert.NotEqual(AuthResponseCode.Approved, auth.Authorization.ResponseCode);
    }

    [Fact]
    public void SetStatus_CancelledCard_CannotReactivate()
    {
        using var harness = new CardServiceTestHarness();
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "CANCEL TEST",
            NationalId = "55555555555",
            CardType = CardType.Credit,
            Limit = 5000,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        harness.SetRole(TestRole.Admin);
        harness.Service.SetCardStatus(new SetCardStatusRequest
        {
            CardId = card.CreatedCard.CardId,
            NewStatus = CardStatus.Cancelled,
            Reason = "Test iptal",
            Channel = "TEST",
            UserId = "admin"
        });
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.SetCardStatus(new SetCardStatusRequest
            {
                CardId = card.CreatedCard.CardId,
                NewStatus = CardStatus.Active,
                Reason = "Yeniden aç",
                Channel = "TEST",
                UserId = "admin"
            }));
        Assert.Equal("CARD_STATE_INVALID", fault.Detail.ErrorCode);
    }

    [Fact]
    public void SetStatus_PendingActivationToActive_Blocked()
    {
        using var harness = new CardServiceTestHarness();
        var cardId = IssuePendingCard(harness);
        harness.SetRole(TestRole.Admin);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.SetCardStatus(new SetCardStatusRequest
            {
                CardId = cardId,
                NewStatus = CardStatus.Active,
                Reason = "Bypass",
                Channel = "TEST",
                UserId = "admin"
            }));
        Assert.Equal("CARD_STATE_INVALID", fault.Detail.ErrorCode);
    }

    [Fact]
    public void SetStatus_ToPendingActivation_Blocked()
    {
        using var harness = new CardServiceTestHarness();
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "STATUS TEST",
            NationalId = "77777777770",
            CardType = CardType.Credit,
            Limit = 5000,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        harness.SetRole(TestRole.Admin);
        var fault = Assert.Throws<FaultException<BankingFault>>(() =>
            harness.Service.SetCardStatus(new SetCardStatusRequest
            {
                CardId = card.CreatedCard.CardId,
                NewStatus = CardStatus.PendingActivation,
                Reason = "Test",
                Channel = "TEST",
                UserId = "admin"
            }));
        Assert.Equal("CARD_STATE_INVALID", fault.Detail.ErrorCode);
    }

    private static int IssuePendingCard(CardServiceTestHarness harness)
    {
        Apply(harness, "11111111111", 50000, 60000);
        harness.SetRole(TestRole.Admin);
        harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "admin" });
        harness.SetRole(TestRole.Teller);
        return harness.Service.GetCardApplications(new GetCardApplicationsRequest { Channel = "TEST", UserId = "t" })
            .Applications.First(a => a.NationalId == "11111111111").CardId!.Value;
    }
}
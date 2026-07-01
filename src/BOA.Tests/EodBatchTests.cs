using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Tests.Infrastructure;
using Microsoft.Data.Sqlite;

namespace BOA.Tests;

public class EodBatchTests
{
    private static (CardServiceTestHarness harness, int cardId) CreateCreditCardWithDebt(decimal limit, decimal spend)
    {
        var harness = new CardServiceTestHarness();
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "BATCH TEST",
            NationalId = "66666666666",
            CardType = CardType.Credit,
            Limit = limit,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        int cardId = card.CreatedCard.CardId;

        if (spend > 0)
        {
            harness.Service.CreateTransaction(new CreateTransactionRequest
            {
                CardId = cardId,
                TransactionType = TransactionType.Purchase,
                Amount = spend,
                Description = "Borç oluştur",
                Pin = "1234",
                Channel = "TEST",
                UserId = "test"
            });
        }

        return (harness, cardId);
    }

    private static void SetStatementDueDate(CardServiceTestHarness harness, int statementId, DateTime dueDate)
    {
        using var conn = new SqliteConnection(harness.ConnectionString);
        conn.Open();
        using var cmd = new SqliteCommand("UPDATE boa_statements SET due_date = @due WHERE statement_id = @id", conn);
        cmd.Parameters.AddWithValue("@due", dueDate.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@id", statementId);
        cmd.ExecuteNonQuery();
    }

    private static void SetCardExpiry(CardServiceTestHarness harness, int cardId, DateTime expiry)
    {
        using var conn = new SqliteConnection(harness.ConnectionString);
        conn.Open();
        using var cmd = new SqliteCommand("UPDATE boa_cards SET expiry_date = @exp WHERE card_id = @id", conn);
        cmd.Parameters.AddWithValue("@exp", expiry.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@id", cardId);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void RunEodBatch_CardWithDebtAndNoOpenStatement_GeneratesStatement()
    {
        var (harness, cardId) = CreateCreditCardWithDebt(limit: 5000, spend: 1000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        var result = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });

        Assert.Equal(1, result.StatementsGenerated);

        harness.SetRole(TestRole.Teller);
        var statements = harness.Service.GetCardStatements(new GetCardStatementsRequest { CardId = cardId, Channel = "TEST" }).Statements;
        var statement = Assert.Single(statements);
        Assert.Equal(1000, statement.TotalDebt);
        Assert.Equal(200, statement.MinimumPayment); // %20 varsayılan oran
        Assert.False(statement.InterestApplied);
    }

    [Fact]
    public void RunEodBatch_CardWithOpenStatement_DoesNotGenerateDuplicateStatement()
    {
        var (harness, cardId) = CreateCreditCardWithDebt(limit: 5000, spend: 1000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });
        var second = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });

        Assert.Equal(0, second.StatementsGenerated);

        harness.SetRole(TestRole.Teller);
        var statements = harness.Service.GetCardStatements(new GetCardStatementsRequest { CardId = cardId, Channel = "TEST" }).Statements;
        Assert.Single(statements);
    }

    [Fact]
    public void RunEodBatch_OverdueStatement_AppliesInterestOnce()
    {
        var (harness, cardId) = CreateCreditCardWithDebt(limit: 5000, spend: 1000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });

        harness.SetRole(TestRole.Teller);
        int statementId = harness.Service.GetCardStatements(new GetCardStatementsRequest { CardId = cardId, Channel = "TEST" }).Statements.Single().StatementId;
        SetStatementDueDate(harness, statementId, DateTime.Now.AddDays(-5)); // gecikmiş ama henüz 30 günü aşmamış

        harness.SetRole(TestRole.Admin);
        var batch2 = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });
        Assert.Equal(1, batch2.InterestAppliedCount);
        Assert.Equal(0, batch2.CardsAutoBlocked); // henüz 30 günü aşmadı

        // Aynı ekstreye tekrar faiz işlenmemeli (idempotency)
        var batch3 = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });
        Assert.Equal(0, batch3.InterestAppliedCount);

        harness.SetRole(TestRole.Teller);
        var updatedCard = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" }).Cards.Single(c => c.CardId == cardId);
        Assert.Equal(-1035, updatedCard.Balance); // 1000 borç + %3.5 faiz
    }

    [Fact]
    public void RunEodBatch_StatementOverdueMoreThan30Days_AutoBlocksCard()
    {
        var (harness, cardId) = CreateCreditCardWithDebt(limit: 5000, spend: 1000);
        using var _ = harness;
        harness.SetRole(TestRole.Admin);

        harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });

        harness.SetRole(TestRole.Teller);
        int statementId = harness.Service.GetCardStatements(new GetCardStatementsRequest { CardId = cardId, Channel = "TEST" }).Statements.Single().StatementId;
        SetStatementDueDate(harness, statementId, DateTime.Now.AddDays(-40));

        harness.SetRole(TestRole.Admin);
        var batch = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });
        Assert.Equal(1, batch.CardsAutoBlocked);

        harness.SetRole(TestRole.Teller);
        var updatedCard = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" }).Cards.Single(c => c.CardId == cardId);
        Assert.Equal(CardStatus.Blocked, updatedCard.Status);
    }

    [Fact]
    public void RunEodBatch_CardNearExpiry_IsAutoRenewed()
    {
        var (harness, cardId) = CreateCreditCardWithDebt(limit: 5000, spend: 0);
        using var _ = harness;

        SetCardExpiry(harness, cardId, DateTime.Now.AddDays(10));

        harness.SetRole(TestRole.Admin);
        var batch = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });
        Assert.Equal(1, batch.CardsRenewed);

        harness.SetRole(TestRole.Teller);
        var renewedCard = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" }).Cards.Single(c => c.CardId == cardId);
        Assert.True(renewedCard.ExpiryDate > DateTime.Now.AddYears(4));
    }

    [Fact]
    public void RunEodBatch_RunTwiceSameDay_IsFullyIdempotent()
    {
        var (harness, _) = CreateCreditCardWithDebt(limit: 5000, spend: 1000);
        using var _h = harness;
        harness.SetRole(TestRole.Admin);

        harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });
        var second = harness.Service.RunEodBatch(new RunEodBatchRequest { Channel = "TEST", UserId = "test" });

        Assert.Equal(0, second.StatementsGenerated);
        Assert.Equal(0, second.InterestAppliedCount);
        Assert.Equal(0, second.CardsAutoBlocked);
        Assert.Equal(0, second.CardsRenewed);
    }
}

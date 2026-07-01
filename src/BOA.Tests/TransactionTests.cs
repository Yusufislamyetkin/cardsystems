using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Tests.Infrastructure;
using CoreWCF;
using BOA.Common.Contracts.Base;

namespace BOA.Tests;

public class TransactionTests
{
    private static (CardServiceTestHarness harness, int cardId) CreateDebitCardWithBalance(decimal initialBalance)
    {
        var harness = new CardServiceTestHarness();
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "TX TEST",
            NationalId = "33333333333",
            CardType = CardType.Debit,
            Limit = 0,
            InitialBalance = initialBalance,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        return (harness, card.CreatedCard.CardId);
    }

    [Fact]
    public void Purchase_InsufficientDebitBalance_IsRejected()
    {
        var (harness, cardId) = CreateDebitCardWithBalance(500);
        using var _ = harness;

        Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 501,
            Description = "Yetersiz bakiye",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        }));
    }

    [Fact]
    public void Purchase_WrongPin_IsRejected()
    {
        var (harness, cardId) = CreateDebitCardWithBalance(1000);
        using var _ = harness;

        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 100,
            Description = "Yanlış PIN",
            Pin = "0000",
            Channel = "TEST",
            UserId = "test"
        }));

        Assert.Equal("INVALID_PIN", fault.Detail.ErrorCode);
    }

    [Fact]
    public void Deposit_IncreasesBalance_WithoutRequiringPin()
    {
        var (harness, cardId) = CreateDebitCardWithBalance(500);
        using var _ = harness;

        var response = harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Deposit,
            Amount = 250,
            Description = "Para yatırma",
            Pin = "", // Deposit PIN gerektirmez
            Channel = "TEST",
            UserId = "test"
        });

        Assert.True(response.IsSuccess);
        Assert.Equal(750, response.UpdatedCard.Balance);
    }

    [Fact]
    public void Fee_PostsAsDebitAndIncreasesDebt_RegardlessOfAvailableLimit()
    {
        // Regresyon: sp_boa_card_create_transaction, Fee (tip 4) için hiçbir dal eşleşmediğinden
        // sessizce hiçbir yevmiye kaydı oluşturmuyordu. Ayrıca Fee, müşteri işlemi gibi limit
        // kontrolüne tabi tutulmamalıdır (banka tarafından zorlanan bir kayıttır).
        var harness = new CardServiceTestHarness();
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "FEE TEST",
            NationalId = "44444444444",
            CardType = CardType.Credit,
            Limit = 1000,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        int cardId = card.CreatedCard.CardId;
        using var _ = harness;

        // Kartı tam limitine kadar kullan (borç = -1000, kullanılabilir limit = 0)
        harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 1000,
            Description = "Limitin tamamı",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        // Batch sürecinin gecikme faizi yansıtması gibi bir Fee işlemi, kullanılabilir limit sıfır
        // olsa bile reddedilmemeli ve kartı limit üzerine taşıyabilmelidir.
        var feeResponse = harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Fee,
            Amount = 35,
            Description = "Gecikme Faizi",
            Pin = "",
            Channel = "BATCH",
            UserId = "SYSTEM_BATCH"
        });

        Assert.True(feeResponse.IsSuccess);
        Assert.Equal(-1035, feeResponse.UpdatedCard.Balance);
    }
}

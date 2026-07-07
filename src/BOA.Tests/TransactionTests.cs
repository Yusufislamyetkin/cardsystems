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
    public void Purchase_BankApprovesButPaycoreDeclines_ReversesLedgerEntryAndThrowsFault()
    {
        // Gerçek bir kart ağında, bankanın kendi kontrolü onaylasa bile provizyon mesajı PayCore'da
        // reddedilirse işlem kalıcılaşamaz. Ledger'a yazılan borç kaydı SİLİNMEZ (immutable), bunun
        // yerine bir ters kayıt (Reversal) ile telafi edilir; bakiye orijinal haline geri döner.
        var (harness, cardId) = CreateDebitCardWithBalance(1000);
        using var _ = harness;
        harness.PaycoreGateway.ForceDeclineNextAuthorization = true;

        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 400,
            Description = "PayCore reddi",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        }));

        Assert.Equal("PAYCORE_DECLINED", fault.Detail.ErrorCode);

        var cardAfter = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" })
            .Cards.Single(c => c.CardId == cardId);
        Assert.Equal(1000, cardAfter.Balance); // Ters kayıt sayesinde bakiye orijinal haline dönmüş olmalı
    }

    [Fact]
    public void Purchase_PaycoreUnreachable_DoesNotReverseAndLeavesTransactionPendingReconciliation()
    {
        // PayCore'a ağ hatası/timeout nedeniyle ulaşılamazsa, PayCore'un GERÇEK kararı bilinmiyor.
        // Bilerek ters kayıt YAZILMAZ (belki PayCore aslında onayladı, yanıt bize ulaşmadı) — ledger
        // kaydı "askıda" (posted ama unconfirmed) kalır, çağırana PAYCORE_UNREACHABLE fault'u döner.
        var (harness, cardId) = CreateDebitCardWithBalance(1000);
        using var _ = harness;
        harness.PaycoreGateway.ThrowOnNextAuthorization = true;

        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 400,
            Description = "PayCore'a ulaşılamadı",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        }));

        Assert.Equal("PAYCORE_UNREACHABLE", fault.Detail.ErrorCode);

        var cardAfter = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" })
            .Cards.Single(c => c.CardId == cardId);
        Assert.Equal(600, cardAfter.Balance); // Ters kayıt YAZILMADI — borç kaydı hâlâ duruyor (bilerek "askıda")
    }

    [Fact]
    public void RetryPendingPaycoreSyncs_ConfirmsPreviouslyUnreachableTransaction()
    {
        // Az önceki senaryonun devamı: PayCore'a ulaşılamayan işlem, mutabakat taraması tekrar
        // çalıştırıldığında (bu sefer PayCore normal cevap veriyor) Confirmed'e geçmelidir.
        var (harness, cardId) = CreateDebitCardWithBalance(1000);
        using var _ = harness;
        harness.PaycoreGateway.ThrowOnNextAuthorization = true;

        Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 400,
            Description = "PayCore'a ulaşılamadı",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        }));

        harness.SetRole(TestRole.Admin);
        var retryResponse = harness.Service.RetryPendingPaycoreSyncs(new RetryPendingPaycoreSyncsRequest { Channel = "TEST", UserId = "admin" });

        Assert.True(retryResponse.IsSuccess);
        Assert.Equal(1, retryResponse.Confirmed);
        Assert.Equal(0, retryResponse.Declined);
        Assert.Equal(0, retryResponse.StillUnreachable);

        harness.SetRole(TestRole.Teller);
        var cardAfter = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" })
            .Cards.Single(c => c.CardId == cardId);
        Assert.Equal(600, cardAfter.Balance); // Onaylandı — hâlâ borçlu, bu doğru; sadece artık PayCore ile senkron
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

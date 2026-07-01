using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Tests.Infrastructure;
using CoreWCF;
using BOA.Common.Contracts.Base;

namespace BOA.Tests;

/// <summary>
/// Regresyon testleri: bu projenin çift kayıt muhasebe modelinde kredi kartı borcu NEGATİF bakiye
/// olarak tutulur (harcama = borca doğru negatife gider). Kredi limiti kontrolü bir keresinde
/// "limit - bakiye" formülünü kullanıyordu; bakiye zaten negatif olduğundan bu formül limiti fiilen
/// hiç uygulamıyordu (limit arttıkça değil, borç arttıkça daha da gevşiyordu). 5000 TL limitli bir
/// kart, 1000 TL kullanıldıktan sonra 4500 TL'lik YENİ bir harcamayı (toplam 5500 TL) kabul
/// edebiliyordu. Doğru formül "limit + bakiye"dir. Bu testler o hatayı bir daha geri getirmeyi engeller.
/// </summary>
public class CreditLimitRegressionTests
{
    private static CardServiceTestHarness CreateCreditCardWithDebt(decimal limit, decimal spend, out int cardId)
    {
        var harness = new CardServiceTestHarness();

        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "LIMIT TEST",
            NationalId = "22222222222",
            CardType = CardType.Credit,
            Limit = limit,
            InitialBalance = 0,
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        cardId = card.CreatedCard.CardId;

        if (spend > 0)
        {
            harness.Service.CreateTransaction(new CreateTransactionRequest
            {
                CardId = cardId,
                TransactionType = TransactionType.Purchase,
                Amount = spend,
                Description = "İlk harcama",
                Pin = "1234",
                Channel = "TEST",
                UserId = "test"
            });
        }

        return harness;
    }

    [Fact]
    public void CreditCard_PurchaseExceedingRemainingLimit_IsRejected()
    {
        using var harness = CreateCreditCardWithDebt(limit: 5000, spend: 1000, out int cardId);

        // Kalan kullanılabilir limit 4000 TL olmalı; 4500 TL'lik yeni harcama reddedilmeli.
        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 4500,
            Description = "Limit üzeri harcama",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        }));

        Assert.Equal("TRANSACTION_CREATE_FAILED", fault.Detail.ErrorCode);
    }

    [Fact]
    public void CreditCard_PurchaseExactlyAtRemainingLimit_IsAccepted()
    {
        using var harness = CreateCreditCardWithDebt(limit: 5000, spend: 1000, out int cardId);

        // Kalan limit tam olarak 4000 TL; bu tutarın tamamını harcamak kabul edilmelidir.
        var response = harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 4000,
            Description = "Limit sınırında harcama",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.True(response.IsSuccess);
        Assert.Equal(-5000, response.UpdatedCard.Balance);
    }

    [Fact]
    public void CreditCard_PurchaseOneUnitOverRemainingLimit_IsRejected()
    {
        using var harness = CreateCreditCardWithDebt(limit: 5000, spend: 1000, out int cardId);

        Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateTransaction(new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 4000.01m,
            Description = "Limit + 1 kuruş",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        }));
    }

    [Fact]
    public void CreditCard_AuthorizeExceedingRemainingLimit_IsDeclinedNotThrown()
    {
        using var harness = CreateCreditCardWithDebt(limit: 5000, spend: 1000, out int cardId);

        // AuthorizeTransaction, business red durumlarını exception olarak değil normal yanıt olarak döner.
        var response = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 4500,
            Description = "Limit üzeri provizyon",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.True(response.IsSuccess); // Çağrının kendisi başarılı, provizyon reddedildi
        Assert.Equal(AuthResponseCode.InsufficientFunds, response.Authorization!.ResponseCode);
        Assert.Equal(AuthorizationStatus.Declined, response.Authorization.Status);
    }

    [Fact]
    public void CreditCard_HeldAuthorization_ReducesAvailableLimitForSubsequentAuthorize()
    {
        using var harness = CreateCreditCardWithDebt(limit: 5000, spend: 0, out int cardId);

        // 4500 TL'lik bir provizyon blokede iken (Capture edilmemiş), kalan kullanılabilir limit 500 TL'dir.
        var firstAuth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 4500,
            Description = "İlk provizyon",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        Assert.Equal(AuthorizationStatus.Authorized, firstAuth.Authorization!.Status);

        var secondAuth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 600,
            Description = "Bloke sonrası ikinci provizyon (600 > kalan 500)",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.Equal(AuthResponseCode.InsufficientFunds, secondAuth.Authorization!.ResponseCode);
    }
}

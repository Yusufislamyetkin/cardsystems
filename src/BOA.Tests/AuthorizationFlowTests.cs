using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Tests.Infrastructure;

namespace BOA.Tests;

public class AuthorizationFlowTests
{
    private static (CardServiceTestHarness harness, int cardId) CreateDebitCard(decimal initialBalance)
    {
        var harness = new CardServiceTestHarness();
        var card = harness.Service.CreateCard(new CreateCardRequest
        {
            CardHolderName = "AUTH TEST",
            NationalId = "55555555555",
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
    public void Authorize_ApprovedRequest_DoesNotAffectLedgerBalanceUntilCaptured()
    {
        var (harness, cardId) = CreateDebitCard(1000);
        using var _ = harness;

        var auth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 200,
            Description = "Market",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.Equal(AuthResponseCode.Approved, auth.Authorization!.ResponseCode);
        Assert.Equal(AuthorizationStatus.Authorized, auth.Authorization.Status);
        Assert.NotNull(auth.Authorization.AuthorizationCode);
        Assert.Equal(6, auth.Authorization.AuthorizationCode!.Length);
        Assert.False(string.IsNullOrWhiteSpace(auth.Authorization.PaycoreAuthReference)); // Banka onayı sonrası PayCore'a da gönderilmiş olmalı

        var cardAfterAuth = harness.Service.GetCardList(new GetCardListRequest { Channel = "TEST" })
            .Cards.Single(c => c.CardId == cardId);
        Assert.Equal(1000, cardAfterAuth.Balance); // Hold, bakiyeyi etkilememeli
    }

    [Fact]
    public void Authorize_BankApprovesButPaycoreDeclines_OverridesToDeclinedAndReleasesHold()
    {
        // Bankanın kendi limit kontrolü onaylayıp bir hold oluştursa bile, gerçek provizyon mesajı
        // PayCore'da reddedilirse banka bu holdü geri almalı — aksi halde iki sistem arasında
        // bankanın tarafında var olmayan bir bloke kalırdı.
        var (harness, cardId) = CreateDebitCard(1000);
        using var _ = harness;
        harness.PaycoreGateway.ForceDeclineNextAuthorization = true;

        var auth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 200,
            Description = "PayCore reddi",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.True(auth.IsSuccess); // Servis çağrısı başarılı; sonuç bir "red" olarak dönüyor (fault değil)
        Assert.Equal(AuthorizationStatus.Declined, auth.Authorization!.Status);
        Assert.Equal(AuthResponseCode.DoNotHonor, auth.Authorization.ResponseCode);
        Assert.Null(auth.Authorization.AuthorizationCode);

        // Hold geri alındığı için tam tutarında yeni bir provizyon alınabilmeli (PayCore tekrar onaylıyor).
        var secondAuth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 1000,
            Description = "PayCore reddi sonrası tam tutar",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        Assert.Equal(AuthResponseCode.Approved, secondAuth.Authorization!.ResponseCode);
    }

    [Fact]
    public void Capture_AppliesLedgerEntryAndMarksAuthorizationCaptured()
    {
        var (harness, cardId) = CreateDebitCard(1000);
        using var _ = harness;

        var auth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 200,
            Description = "Market",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });

        var capture = harness.Service.CaptureAuthorization(new CaptureAuthorizationRequest
        {
            AuthorizationId = auth.Authorization!.AuthorizationId,
            Channel = "TEST",
            UserId = "test"
        });

        Assert.True(capture.IsSuccess);
        Assert.Equal(800, capture.UpdatedCard!.Balance);
        Assert.Equal(AuthorizationStatus.Captured, capture.Authorization!.Status);
    }

    [Fact]
    public void Void_DoesNotAffectBalanceAndReleasesHold()
    {
        var (harness, cardId) = CreateDebitCard(1000);
        using var _ = harness;

        var auth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 900,
            Description = "İptal edilecek provizyon",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        Assert.Equal(AuthorizationStatus.Authorized, auth.Authorization!.Status);

        var voidResponse = harness.Service.VoidAuthorization(new VoidAuthorizationRequest
        {
            AuthorizationId = auth.Authorization.AuthorizationId,
            Reason = "Müşteri vazgeçti",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.True(voidResponse.IsSuccess);
        Assert.Equal(AuthorizationStatus.Voided, voidResponse.Authorization!.Status);

        // Hold serbest bırakıldığı için tam bakiye tutarında yeni bir provizyon alınabilmeli.
        var secondAuth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 1000,
            Description = "Void sonrası tam tutar",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        Assert.Equal(AuthResponseCode.Approved, secondAuth.Authorization!.ResponseCode);
    }

    [Fact]
    public void Authorize_WrongPin_IsDeclinedWithIncorrectPinCode()
    {
        var (harness, cardId) = CreateDebitCard(1000);
        using var _ = harness;

        var auth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 100,
            Description = "Yanlış PIN",
            Pin = "9999",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.Equal(AuthResponseCode.IncorrectPin, auth.Authorization!.ResponseCode);
        Assert.Equal(AuthorizationStatus.Declined, auth.Authorization.Status);
    }

    [Fact]
    public void Capture_AlreadyVoidedAuthorization_ThrowsFault()
    {
        var (harness, cardId) = CreateDebitCard(1000);
        using var _ = harness;

        var auth = harness.Service.AuthorizeTransaction(new AuthorizeTransactionRequest
        {
            CardId = cardId,
            TransactionType = TransactionType.Purchase,
            Amount = 100,
            Description = "Test",
            Pin = "1234",
            Channel = "TEST",
            UserId = "test"
        });
        harness.Service.VoidAuthorization(new VoidAuthorizationRequest
        {
            AuthorizationId = auth.Authorization!.AuthorizationId,
            Reason = "Test",
            Channel = "TEST",
            UserId = "test"
        });

        Assert.ThrowsAny<Exception>(() => harness.Service.CaptureAuthorization(new CaptureAuthorizationRequest
        {
            AuthorizationId = auth.Authorization.AuthorizationId,
            Channel = "TEST",
            UserId = "test"
        }));
    }
}

using BOA.Common.Contracts.Enums;
using BOA.Services.Card.Paycore;

namespace BOA.Tests.Infrastructure;

/// <summary>
/// Testlerin, gerçek bir dış sisteme (PayCore) veya dosya tabanlı bir mock veritabanına ihtiyaç
/// duymadan, dış entegrasyon sınırının davranışını (onay/red) deterministik şekilde kontrol edebilmesi
/// için kullanılan sahte (fake) bir IPaycoreGateway uygulamasıdır. Varsayılan davranış "her zaman
/// onayla" — tıpkı gerçek hayattaki PayCore'un büyük çoğunlukla sorunsuz çalışması gibi.
/// </summary>
public sealed class FakePaycoreGateway : IPaycoreGateway
{
    public bool ForceDeclineNextAuthorization { get; set; }

    /// <summary>
    /// Test amaçlı: bir sonraki Authorize çağrısında gerçek bir ağ hatası/timeout'u simüle etmek için
    /// açık bir onay/red DÖNMEZ, doğrudan exception fırlatır. ForceDeclineNextAuthorization'dan farkı
    /// bu — "reddedildi" ile "hiç yanıt alınamadı" birbirinden ayrı test edilebilsin diye.
    /// </summary>
    public bool ThrowOnNextAuthorization { get; set; }

    public bool ThrowOnNextIssueCard { get; set; }
    public bool ThrowOnNextSetCardStatus { get; set; }
    public bool ThrowOnNextRenewCard { get; set; }

    public int IssueCardCallCount { get; private set; }
    public int SetCardStatusCallCount { get; private set; }
    public int RenewCardCallCount { get; private set; }

    public PaycoreCardResult IssueCard(string maskedPan, string cardHolderName, CardType cardType)
    {
        IssueCardCallCount++;
        if (ThrowOnNextIssueCard)
        {
            ThrowOnNextIssueCard = false;
            throw new TimeoutException("Test amaçlı simüle edilmiş PayCore IssueCard hatası.");
        }
        return new() { IsSuccess = true, PaycoreCardReference = "FAKE-CARD-" + Guid.NewGuid().ToString("N")[..12] };
    }

    public PaycoreResult SetCardStatus(string paycoreCardReference, CardStatus newStatus)
    {
        SetCardStatusCallCount++;
        if (ThrowOnNextSetCardStatus)
        {
            ThrowOnNextSetCardStatus = false;
            throw new TimeoutException("Test amaçlı simüle edilmiş PayCore SetCardStatus hatası.");
        }
        return new() { IsSuccess = true };
    }

    public PaycoreResult RenewCard(string paycoreCardReference, DateTime newExpiryDate)
    {
        RenewCardCallCount++;
        if (ThrowOnNextRenewCard)
        {
            ThrowOnNextRenewCard = false;
            throw new TimeoutException("Test amaçlı simüle edilmiş PayCore RenewCard hatası.");
        }
        return new() { IsSuccess = true };
    }

    public PaycoreAuthResult Authorize(string paycoreCardReference, decimal amount, string bankReferenceNumber)
    {
        if (ThrowOnNextAuthorization)
        {
            ThrowOnNextAuthorization = false;
            throw new TimeoutException("Test amaçlı simüle edilmiş PayCore ağ hatası/timeout.");
        }

        bool approve = !ForceDeclineNextAuthorization;
        ForceDeclineNextAuthorization = false;

        return approve
            ? new PaycoreAuthResult { IsApproved = true, PaycoreAuthReference = "FAKE-AUTH-" + Guid.NewGuid().ToString("N")[..12], ResponseCode = "00" }
            : new PaycoreAuthResult { IsApproved = false, ResponseCode = "05", ErrorMessage = "Test amaçlı zorlanmış PayCore reddi." };
    }

    public PaycoreResult UpdateLimit(string paycoreCardReference, decimal newLimit)
        => new() { IsSuccess = true };

    public PaycoreResult Capture(string paycoreAuthReference) => new() { IsSuccess = true };

    public PaycoreResult Void(string paycoreAuthReference) => new() { IsSuccess = true };
}

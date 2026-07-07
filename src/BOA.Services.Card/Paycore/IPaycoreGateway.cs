using BOA.Common.Contracts.Enums;

namespace BOA.Services.Card.Paycore;

/// <summary>
/// Bankanın kendi çekirdek sistemi (limit/bakiye kararı, operasyonel kart kayıtları) ile
/// PayCore gibi dış bir kart işleme sağlayıcısı (switch bağlantısı, gerçek HSM, kart yaşam
/// döngüsü mekaniği) arasındaki entegrasyon sınırını temsil eder.
///
/// CardService, kendi iş kurallarını ve limit kontrolünü HER ZAMAN önce kendi tarafında
/// çalıştırır; bu arayüz ancak bankanın kendi kararı olumluyken devreye girer. Bu iki
/// sistemin farklı sonuca varabileceğini (banka onayladı ama PayCore reddetti gibi) ve
/// aralarında gerçek bir ağ/entegrasyon sınırı olduğunu modellemek buradaki temel amaçtır.
/// </summary>
public interface IPaycoreGateway
{
    /// <summary>Kart yaşam döngüsü kaydını PayCore tarafında açar (issuing sonrası adım).</summary>
    PaycoreCardResult IssueCard(string maskedPan, string cardHolderName, CardType cardType);

    /// <summary>Kartın durum değişikliğini (blokaj/aktivasyon/iptal) PayCore tarafına yansıtır.</summary>
    PaycoreResult SetCardStatus(string paycoreCardReference, CardStatus newStatus);

    /// <summary>Kart yenilemesini (son kullanma tarihi uzatma) PayCore tarafına yansıtır.</summary>
    PaycoreResult RenewCard(string paycoreCardReference, DateTime newExpiryDate);

    /// <summary>
    /// Kartın (onaylanmış) yeni limitini PayCore tarafına yansıtır. Gerçek işlemciler stand-in
    /// authorization sırasında kendi tuttukları limiti kullanır; senkronize edilmezse banka
    /// erişilemezken işlemci ESKİ limitle onay verirdi.
    /// </summary>
    PaycoreResult UpdateLimit(string paycoreCardReference, decimal newLimit);

    /// <summary>
    /// Bankanın kendi limit kontrolü onayladıktan SONRA çağrılır — gerçek provizyon mesajının
    /// switch/PayCore tarafında da onaylanıp onaylanmadığını sorar.
    /// </summary>
    PaycoreAuthResult Authorize(string paycoreCardReference, decimal amount, string bankReferenceNumber);

    /// <summary>Daha önce onaylanmış bir provizyonun PayCore tarafında da kesinleştirilmesini sağlar.</summary>
    PaycoreResult Capture(string paycoreAuthReference);

    /// <summary>Daha önce onaylanmış bir provizyonun PayCore tarafındaki bloğunun kaldırılmasını sağlar.</summary>
    PaycoreResult Void(string paycoreAuthReference);
}

public class PaycoreCardResult
{
    public bool IsSuccess { get; set; }
    public string? PaycoreCardReference { get; set; }
    public string? ErrorMessage { get; set; }
}

public class PaycoreAuthResult
{
    public bool IsApproved { get; set; }
    public string? PaycoreAuthReference { get; set; }

    /// <summary>PayCore'un kendi tarafında döndürdüğü, ISO 8583 DE39 benzeri yanıt kodu.</summary>
    public string ResponseCode { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
}

public class PaycoreResult
{
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

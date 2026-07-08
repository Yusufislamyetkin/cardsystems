using System.Runtime.Serialization;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Dtos;

/// <summary>
/// Kart bilgilerini taşıyan Veri Transfer Nesnesidir (DTO).
/// WCF DataContract kurallarına uygun olarak tasarlanmıştır.
/// </summary>
[DataContract]
public class CardDto
{
    /// <summary>
    /// Kartın sistemdeki benzersiz birincil anahtarı (ID).
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// Kartın 16 haneli numarası (Örn: "4355123456789012").
    /// Güvenlik açısından UI katmanına iletilirken maskelenmiş hali tercih edilebilir.
    /// </summary>
    [DataMember]
    public string CardNumber { get; set; } = string.Empty;

    /// <summary>
    /// Kart sahibinin adı soyadı.
    /// </summary>
    [DataMember]
    public string CardHolderName { get; set; } = string.Empty;

    /// <summary>
    /// Kart sahibinin EMBOSS standardında formatlanmış adı (max 21 karakter, uppercase, Türkçe karakterler ASCII'ye dönüştürülmüş).
    /// Kart basımında kullanılır.
    /// </summary>
    [DataMember]
    public string EmbossName { get; set; } = string.Empty;

    /// <summary>
    /// Kartın Tipi (Banka Kartı veya Kredi Kartı).
    /// </summary>
    [DataMember]
    public CardType CardType { get; set; }

    /// <summary>
    /// Kartın ait olduğu kart ağı markası (Visa, Mastercard, Troy, Amex).
    /// BIN numarasına göre otomatik belirlenir.
    /// </summary>
    [DataMember]
    public CardBrand CardBrand { get; set; }

    /// <summary>
    /// Kartın ürün segmenti (Classic, Gold, Platinum, Business, Premium).
    /// Limit tavanı ve ek hizmetler bu segmente göre belirlenir.
    /// </summary>
    [DataMember]
    public CardProduct CardProduct { get; set; }

    /// <summary>
    /// Kartın son kullanma tarihi (MM/YY formatında sunulmak üzere saklanır).
    /// Ayın son gününe normalize edilmiştir.
    /// </summary>
    [DataMember]
    public DateTime ExpiryDate { get; set; }

    /// <summary>
    /// Kartın güncel durumu (Aktif, Bloke, İptal, PendingActivation, InTransit).
    /// </summary>
    [DataMember]
    public CardStatus Status { get; set; }

    /// <summary>
    /// Kredi kartları için tanımlı toplam harcama (kredi) limiti. Banka kartlarında (Debit) kredi
    /// limiti kavramı yoktur; bu alan her zaman 0'dır. Günlük nakit çekim limiti bundan ayrı bir
    /// kuraldır ve işlem anında (tüm kart türleri için) ayrıca kontrol edilir.
    /// </summary>
    [DataMember]
    public decimal CardLimit { get; set; }

    /// <summary>
    /// Kartın güncel kullanılabilir bakiyesi.
    /// Kredi kartı için kullanılabilir limit; banka kartı için hesaptaki bakiye anlamına gelir.
    /// </summary>
    [DataMember]
    public decimal Balance { get; set; }

    /// <summary>
    /// Kartın sisteme ilk tanımlandığı tarih.
    /// </summary>
    [DataMember]
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// Kartın bağlı olduğu müşterinin benzersiz anahtarı (Foreign Key).
    /// </summary>
    [DataMember]
    public int CustomerId { get; set; }

    /// <summary>
    /// Kartın bağlı olduğu vadesiz/kredi hesabının benzersiz anahtarı (Foreign Key).
    /// </summary>
    [DataMember]
    public int BankAccountId { get; set; }

    /// <summary>
    /// Kart hamilinin T.C. Kimlik Numarası.
    /// </summary>
    [DataMember]
    public string NationalId { get; set; } = string.Empty;

    /// <summary>
    /// Bu kartın PayCore (dış kart işleme sağlayıcısı) tarafındaki karşılık gelen referansı.
    /// Bankanın kendi kart kaydı ile PayCore'daki kayıt arasındaki tek bağ budur.
    /// </summary>
    [DataMember]
    public string? PaycoreReference { get; set; }

    /// <summary>
    /// Kartın arkasındaki 3 haneli CVV2 güvenlik kodunun hash'lenmiş değeri.
    /// Gerçek CVV2 değeri asla düz metin olarak saklanmaz — yalnızca HSM'den türetilmiş hash
    /// tutulur. İstemciye iletilmez (PCI-DSS gereği); yalnızca HSM doğrulama akışında kullanılır.
    /// </summary>
    [DataMember]
    public string? Cvv2Hash { get; set; }

    /// <summary>
    /// Manyetik şerit CVV değerinin hash'lenmiş karşılığı.
    /// </summary>
    [DataMember]
    public string? CvvHash { get; set; }

    /// <summary>
    /// EMV Service Code (3 hane). Kartın kullanım kısıtlamalarını belirtir.
    /// Örn: "201" = Chip + International, normal authorization, PIN required.
    /// </summary>
    [DataMember]
    public string ServiceCode { get; set; } = "201";

    /// <summary>
    /// Track2 eşdeğer verisi (PAN + Expiry + Service Code + PVKI + CVV + padding).
    /// Manyetik şeritli ve temassız işlemlerde kullanılır.
    /// </summary>
    [DataMember]
    public string? Track2Data { get; set; }

    /// <summary>
    /// Kart numarasını PCI-DSS uyumlu şekilde maskeleyerek döner.
    /// Örnek: "4355 12** **** 9012"
    /// </summary>
    public string MaskedCardNumber
    {
        get
        {
            if (string.IsNullOrEmpty(CardNumber) || CardNumber.Length < 16)
                return CardNumber;
            // Eğer numara zaten maskeli geldiyse doğrudan formatlayıp döneriz
            if (CardNumber.Contains("*"))
            {
                return CardNumber.Length == 16
                    ? $"{CardNumber.Substring(0, 4)} {CardNumber.Substring(4, 2)}** **** {CardNumber.Substring(12, 4)}"
                    : CardNumber;
            }
            return $"{CardNumber.Substring(0, 4)} {CardNumber.Substring(4, 2)}** **** {CardNumber.Substring(12, 4)}";
        }
    }

    /// <summary>
    /// Kart numarasının son 4 hanesini döndürür.
    /// </summary>
    public string Last4Digits
    {
        get
        {
            if (string.IsNullOrEmpty(CardNumber) || CardNumber.Length < 4)
                return "****";
            string sanitized = CardNumber.Replace(" ", "").Replace("*", "");
            return sanitized.Length >= 4 ? sanitized.Substring(sanitized.Length - 4) : sanitized;
        }
    }

    /// <summary>
    /// Karta ait Defter-i Kebir (GL) Muhasebe Hesap Numarası.
    /// </summary>
    public string GlAccountNumber
    {
        get
        {
            if (string.IsNullOrEmpty(CardNumber) || CardNumber.Length < 4)
                return "GL-CARD-PENDING";
            string last4 = CardNumber.Substring(CardNumber.Length - 4);
            return $"GL-CARD-{CreatedDate:yyyyMMdd}-{last4}";
        }
    }

    /// <summary>
    /// Son kullanma tarihini MM/YY formatında döndürür (kart ön yüz gösterimi için).
    /// </summary>
    public string ExpiryDisplay => ExpiryDate.ToString("MM/yy");

    [DataMember] public decimal CashAdvanceLimit { get; set; }
    [DataMember] public decimal InstallmentLimit { get; set; }
    [DataMember] public decimal DailyAtmLimit { get; set; }
    [DataMember] public decimal DailyPosLimit { get; set; }
    [DataMember] public decimal MonthlySpendingLimit { get; set; }
    [DataMember] public decimal? TemporaryLimitAmount { get; set; }
    [DataMember] public DateTime? TemporaryLimitExpiry { get; set; }

    /// <summary>
    /// Kartın bloke edilme nedeni. Sadece status Blocked iken anlamlıdır.
    /// </summary>
    [DataMember]
    public BlockReason? BlockReason { get; set; }

    /// <summary>
    /// Kartın bloke edildiği tarih.
    /// </summary>
    [DataMember]
    public DateTime? BlockedDate { get; set; }

    /// <summary>
    /// Kartın iptal edildiği tarih. Sadece status Cancelled iken anlamlıdır.
    /// </summary>
    [DataMember]
    public DateTime? CancelledDate { get; set; }

    /// <summary>
    /// Kartın iptal edilme nedeni. Sadece status Cancelled iken anlamlıdır.
    /// </summary>
    [DataMember]
    public CancellationReason? CancellationReason { get; set; }

    /// <summary>
    /// Yenileme/reissue zincirinde bu kartın öncesindeki kartın ID'si.
    /// Yeni kartın previous_card_id'si, eski kartına işaret eder.
    /// </summary>
    [DataMember]
    public int? PreviousCardId { get; set; }

    /// <summary>
    /// Yenileme/reissue zincirinde bu kartı devralan yeni kartın ID'si.
    /// Eski kartın replaced_by_card_id'si, yeni karta işaret eder.
    /// </summary>
    [DataMember]
    public int? ReplacedByCardId { get; set; }
}

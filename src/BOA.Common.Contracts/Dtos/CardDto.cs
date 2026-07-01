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
    /// Kartın Tipi (Banka Kartı veya Kredi Kartı).
    /// </summary>
    [DataMember]
    public CardType CardType { get; set; }

    /// <summary>
    /// Kartın son kullanma tarihi (MM/YY formatında sunulmak üzere saklanır).
    /// </summary>
    [DataMember]
    public DateTime ExpiryDate { get; set; }

    /// <summary>
    /// Kartın güncel durumu (Aktif, Bloke, İptal).
    /// </summary>
    [DataMember]
    public CardStatus Status { get; set; }

    /// <summary>
    /// Kartın tanımlı toplam limiti (Kredi kartları için harcama limiti, banka kartlarında günlük çekim limiti).
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
    /// Kart numarasını güvenlik standartlarına (PCI-DSS) uygun şekilde maskeleyerek döner.
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
}

using System.Runtime.Serialization;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Dtos;

/// <summary>
/// Kart hareketlerini temsil eden Veri Transfer Nesnesidir.
/// </summary>
[DataContract]
public class TransactionDto
{
    /// <summary>
    /// Hareketin sistemdeki benzersiz birincil anahtarı (ID).
    /// </summary>
    [DataMember]
    public int TransactionId { get; set; }

    /// <summary>
    /// Hareketin ait olduğu kartın benzersiz anahtarı (Foreign Key).
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// İşlem Türü (Alışveriş, Para Yatırma, Nakit Çekme, Ücret).
    /// </summary>
    [DataMember]
    public TransactionType TransactionType { get; set; }

    /// <summary>
    /// İşlem Tutarı (Örn: 150.50). Borç/Alacak ayrımı işlem türüne göre veya eksi/artı işaretle ayırt edilebilir.
    /// </summary>
    [DataMember]
    public decimal Amount { get; set; }

    /// <summary>
    /// İşleme ait açıklama (Örn: "Emlak Katilim Ortak ATM Nakit Cekim", "Market Alisverisi").
    /// </summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// İşlemin gerçekleştiği tarih ve saat.
    /// </summary>
    [DataMember]
    public DateTime TransactionDate { get; set; }

    /// <summary>
    /// Bankacılık provizyon veya işlem referans numarası (RefNo).
    /// Mutabakat süreçlerinde (reconciliation) sistemi eşleştirmek için kullanılır.
    /// </summary>
    [DataMember]
    public string ReferenceNumber { get; set; } = string.Empty;

    /// <summary>
    /// İşlemin gerçekleştiği üye iş yerinin kimliği (BKM/switch tarafında eşleştirme için). Kart-içi işlemlerde (yatırma, ücret) boş olabilir.
    /// </summary>
    [DataMember]
    public string? MerchantId { get; set; }

    /// <summary>
    /// Üye iş yeri kategori kodu (Merchant Category Code, ISO 8583 DE18).
    /// </summary>
    [DataMember]
    public string? Mcc { get; set; }
}

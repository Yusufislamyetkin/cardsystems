using System.Runtime.Serialization;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Dtos;

/// <summary>
/// Kart bloke geçmişi kaydını taşıyan Veri Transfer Nesnesidir (DTO).
/// </summary>
[DataContract]
public class CardBlockHistoryDto
{
    /// <summary>
    /// Bloke geçmişi kaydının benzersiz anahtarı.
    /// </summary>
    [DataMember]
    public int BlockHistoryId { get; set; }

    /// <summary>
    /// Bloke edilen kartın sistemdeki ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// Kartın bloke edilme nedeni (Kayıp, Çalıntı, Fraud vb.).
    /// </summary>
    [DataMember]
    public BlockReason BlockReason { get; set; }

    /// <summary>
    /// Acil durum blokesi olup olmadığı.
    /// </summary>
    [DataMember]
    public bool IsEmergency { get; set; }

    /// <summary>
    /// Bloke işleminin açıklaması.
    /// </summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Çalıntı durumunda polis tutanak numarası (opsiyonel).
    /// </summary>
    [DataMember]
    public string? PoliceReportNumber { get; set; }

    /// <summary>
    /// Son bilinen işlem referansı (opsiyonel).
    /// </summary>
    [DataMember]
    public string? LastKnownTransactionRef { get; set; }

    /// <summary>
    /// Yedek kart talep edilip edilmediği.
    /// </summary>
    [DataMember]
    public bool ReplacementRequested { get; set; }

    /// <summary>
    /// Talep edilen yedek kartın ID'si (varsa).
    /// </summary>
    [DataMember]
    public int? ReplacementCardId { get; set; }

    /// <summary>
    /// İşlemi yapan kullanıcı ID'si.
    /// </summary>
    [DataMember]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Bloke işleminin oluşturulma tarihi.
    /// </summary>
    [DataMember]
    public DateTime CreatedDate { get; set; }
}
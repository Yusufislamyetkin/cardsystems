using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Kayıp/çalıntı kart bildirimi için parametreleri içeren request sınıfıdır.
/// </summary>
[DataContract]
public class ReportLostStolenRequest : RequestBase
{
    /// <summary>
    /// Bildirimi yapılan kartın sistemdeki ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// Bloke nedeni (sadece LostCard, StolenCard, FraudSuspicion kabul eder).
    /// </summary>
    [DataMember]
    public BlockReason BlockReason { get; set; }

    /// <summary>
    /// Olay açıklaması (zorunlu).
    /// </summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Çalıntı durumunda polis tutanak numarası (çalıntı için zorunlu, kayıp için opsiyonel).
    /// </summary>
    [DataMember]
    public string? PoliceReportNumber { get; set; }

    /// <summary>
    /// Son kullanılan işlem referansı (opsiyonel).
    /// </summary>
    [DataMember]
    public string? LastKnownTransactionRef { get; set; }

    /// <summary>
    /// Yedek kart talep edilsin mi?
    /// </summary>
    [DataMember]
    public bool RequestReplacement { get; set; }
}

/// <summary>
/// Kayıp/çalıntı bildirimi işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class ReportLostStolenResponse : ResponseBase
{
    /// <summary>
    /// Bloke edilen kartın güncel detayları.
    /// </summary>
    [DataMember]
    public CardDto BlockedCard { get; set; }

    /// <summary>
    /// Oluşturulan bloke geçmişi kaydı.
    /// </summary>
    [DataMember]
    public CardBlockHistoryDto BlockHistory { get; set; }

    /// <summary>
    /// İptal edilen aktif provizyon sayısı.
    /// </summary>
    [DataMember]
    public int VoidedAuthorizationCount { get; set; }

    /// <summary>
    /// Yedek kart talep edildiyse, oluşturulan yeni kartın ID'si.
    /// </summary>
    [DataMember]
    public int? ReplacementCardId { get; set; }
}
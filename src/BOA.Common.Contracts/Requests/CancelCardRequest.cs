using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Kart iptali için parametreleri içeren request sınıfıdır.
/// </summary>
[DataContract]
public class CancelCardRequest : RequestBase
{
    /// <summary>
    /// İptal edilecek kartın sistemdeki ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// İptal nedeni.
    /// </summary>
    [DataMember]
    public CancellationReason CancellationReason { get; set; }

    /// <summary>
    /// İptal gerekçesi detayı (zorunlu).
    /// </summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Bakiye/borç varsa "farkındayım" onayı. Borçlu kart iptalinde bu true olmalıdır.
    /// </summary>
    [DataMember]
    public bool AcknowledgeOutstandingBalance { get; set; }
}

/// <summary>
/// Kart iptal işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class CancelCardResponse : ResponseBase
{
    /// <summary>
    /// İptal edilen kartın güncel detayları.
    /// </summary>
    [DataMember]
    public CardDto CancelledCard { get; set; }

    /// <summary>
    /// İptal anındaki borç tutarı (varsa).
    /// </summary>
    [DataMember]
    public decimal OutstandingBalance { get; set; }

    /// <summary>
    /// İptal edilen aktif provizyon sayısı.
    /// </summary>
    [DataMember]
    public int VoidedAuthorizationCount { get; set; }

    /// <summary>
    /// Final ekstre oluşturuldu mu?
    /// </summary>
    [DataMember]
    public bool FinalStatementGenerated { get; set; }
}
using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Kart yeniden basım için parametreleri içeren request sınıfıdır.
/// </summary>
[DataContract]
public class ReissueCardRequest : RequestBase
{
    /// <summary>
    /// Yeniden basılacak kartın sistemdeki ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// Yeniden basım nedeni.
    /// </summary>
    [DataMember]
    public ReissueReason ReissueReason { get; set; }

    /// <summary>
    /// İsim değişikliğinde yeni ad (null → mevcut ad korunur).
    /// </summary>
    [DataMember]
    public string? NewCardHolderName { get; set; }

    /// <summary>
    /// Upgrade/downgrade'de yeni ürün (null → mevcut ürün korunur).
    /// </summary>
    [DataMember]
    public CardProduct? NewCardProduct { get; set; }

    /// <summary>
    /// Yeniden basım açıklaması (zorunlu).
    /// </summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Kart yeniden basım işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class ReissueCardResponse : ResponseBase
{
    /// <summary>
    /// Eski kart bilgisi (Cancelled olarak güncellendi).
    /// </summary>
    [DataMember]
    public CardDto OldCard { get; set; }

    /// <summary>
    /// PendingActivation durumda yeni kart bilgisi.
    /// </summary>
    [DataMember]
    public CardDto NewCard { get; set; }
}
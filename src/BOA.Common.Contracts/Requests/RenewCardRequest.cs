using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Kart yenileme için parametreleri içeren request sınıfıdır.
/// </summary>
[DataContract]
public class RenewCardRequest : RequestBase
{
    /// <summary>
    /// Yenilenecek kartın sistemdeki ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }
}

/// <summary>
/// Kart yenileme işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class RenewCardResponse : ResponseBase
{
    /// <summary>
    /// Eski kart bilgisi (status hâlâ Active — yeni kart aktive edilince iptal olacak).
    /// </summary>
    [DataMember]
    public CardDto OldCard { get; set; }

    /// <summary>
    /// PendingActivation durumda yeni kart bilgisi.
    /// </summary>
    [DataMember]
    public CardDto NewCard { get; set; }
}
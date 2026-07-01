using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Bir kartın limitini güncellemek için gönderilecek parametreleri içeren sınıftır.
/// </summary>
[DataContract]
public class UpdateCardLimitRequest : RequestBase
{
    /// <summary>
    /// Limiti güncellenecek kartın sistemdeki benzersiz ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// Kartın tanımlanması istenen yeni limit değeri.
    /// Negatif olamaz.
    /// </summary>
    [DataMember]
    public decimal NewLimit { get; set; }
}

/// <summary>
/// Limit güncelleme işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class UpdateCardLimitResponse : ResponseBase
{
    /// <summary>
    /// Limit güncelleme sonrası güncel kart detayları.
    /// </summary>
    [DataMember]
    public CardDto UpdatedCard { get; set; }
}

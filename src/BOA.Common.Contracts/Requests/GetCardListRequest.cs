using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Kartları listelemek ve filtrelemek için gönderilecek parametreleri içeren sınıftır.
/// </summary>
[DataContract]
public class GetCardListRequest : RequestBase
{
    /// <summary>
    /// Kart hamilinin adına göre filtreleme (Kısmi veya tam eşleşme aranır).
    /// Null veya boş ise filtreleme yapılmaz.
    /// </summary>
    [DataMember]
    public string? CardHolderNameFilter { get; set; }

    /// <summary>
    /// Kart tipine göre filtreleme (Banka veya Kredi Kartı).
    /// Null ise tüm kartlar çekilir.
    /// </summary>
    [DataMember]
    public CardType? CardTypeFilter { get; set; }

    /// <summary>
    /// Kart durumuna göre filtreleme (Aktif, Bloke vb.).
    /// Null ise tüm kartlar çekilir.
    /// </summary>
    [DataMember]
    public CardStatus? StatusFilter { get; set; }
}

/// <summary>
/// Kart listeleme işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class GetCardListResponse : ResponseBase
{
    /// <summary>
    /// Filtrelere uygun olan kartların listesidir.
    /// </summary>
    [DataMember]
    public List<CardDto> Cards { get; set; } = new List<CardDto>();
}

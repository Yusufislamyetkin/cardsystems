using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Bir kartın durumunu değiştirmek (Aktifleştirme, Bloke etme, İptal) için parametreleri içeren sınıftır.
/// </summary>
[DataContract]
public class SetCardStatusRequest : RequestBase
{
    /// <summary>
    /// Durumu güncellenecek kartın sistemdeki ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// Kartın geçirilmesi istenen yeni durumu (Active, Blocked, Cancelled).
    /// </summary>
    [DataMember]
    public CardStatus NewStatus { get; set; }

    /// <summary>
    /// Durum değişikliğinin nedeni (Örn: "Kayip-Calinti", "Musteri Talebi", "Borc Gecikmesi").
    /// Audit loglarında saklanmak üzere veritabanına iletilir.
    /// </summary>
    [DataMember]
    public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// Durum değiştirme işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class SetCardStatusResponse : ResponseBase
{
    /// <summary>
    /// Durum değişikliği sonrası güncel kart detayları.
    /// </summary>
    [DataMember]
    public CardDto UpdatedCard { get; set; }
}

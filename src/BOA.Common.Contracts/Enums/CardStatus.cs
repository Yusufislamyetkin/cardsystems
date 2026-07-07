using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kartın yaşam döngüsü içerisindeki durumlarını temsil eden numaralandırmadır.
/// </summary>
[DataContract]
public enum CardStatus
{
    /// <summary>
    /// Kart aktif ve alışveriş/nakit çekim işlemlerine açık durumdadır.
    /// </summary>
    [EnumMember]
    Active = 1,

    /// <summary>
    /// Kart geçici olarak kullanıma kapatılmıştır (Kayıp/Çalıntı şüphesi, borç gecikmesi vb.).
    /// </summary>
    [EnumMember]
    Blocked = 2,

    /// <summary>
    /// Kart kalıcı olarak iptal edilmiştir. Yeniden aktifleştirilemez.
    /// </summary>
    [EnumMember]
    Cancelled = 3,

    /// <summary>
    /// Kart basılmış/emboss edilmiş ancak henüz müşteri aktivasyonu yapılmamıştır; işleme kapalıdır.
    /// </summary>
    [EnumMember]
    PendingActivation = 4,

    /// <summary>
    /// Kart basıldı, kargo/şube teslimatında. Müşteri henüz teslim almadı.
    /// </summary>
    [EnumMember]
    InTransit = 5
}

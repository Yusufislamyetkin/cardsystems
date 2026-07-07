using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kart ürün segmentini belirtir. Kartın limit aralıkları, yıllık ücret politikası,
/// ek hizmetler ve puanlama kategorileri bu segmente göre farklılaşır.
/// </summary>
[DataContract]
public enum CardProduct
{
    /// <summary>Standart/Classic segment — temel kart özellikleri</summary>
    [EnumMember]
    Classic = 1,

    /// <summary>Gold segment — orta segment, genişletilmiş limit ve hizmetler</summary>
    [EnumMember]
    Gold = 2,

    /// <summary>Platinum segment — üst segment, yüksek limit, seyahat ve concierge hizmetleri</summary>
    [EnumMember]
    Platinum = 3,

    /// <summary>Business/Corporate — ticari kart segmenti</summary>
    [EnumMember]
    Business = 4,

    /// <summary>Infinite/World Elite — ultra premium segment</summary>
    [EnumMember]
    Premium = 5
}
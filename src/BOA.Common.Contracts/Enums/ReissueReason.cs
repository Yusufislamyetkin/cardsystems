using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kart yeniden basım nedenini temsil eden numaralandırmadır.
/// </summary>
[DataContract]
public enum ReissueReason
{
    /// <summary>
    /// Fiziksel hasar (kırık, bükülme, manyetik bozulma).
    /// </summary>
    [EnumMember]
    PhysicalDamage = 1,

    /// <summary>
    /// Ad/soyad değişikliği (evlilik, mahkeme kararı).
    /// </summary>
    [EnumMember]
    NameChange = 2,

    /// <summary>
    /// Ürün yükseltme (Classic→Gold, Gold→Platinum).
    /// </summary>
    [EnumMember]
    CardUpgrade = 3,

    /// <summary>
    /// Ürün düşürme.
    /// </summary>
    [EnumMember]
    CardDowngrade = 4,

    /// <summary>
    /// Çip arızası (EMV okuma hatası).
    /// </summary>
    [EnumMember]
    ChipMalfunction = 5,

    /// <summary>
    /// Güvenlik endişesi (veri ihlali sonrası proaktif değişim).
    /// </summary>
    [EnumMember]
    SecurityConcern = 6
}
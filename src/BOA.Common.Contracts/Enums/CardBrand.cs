using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kartın ait olduğu uluslararası/ulusal kart ağı markasını belirtir.
/// BIN (Issuer Identification Number) tablosundaki ilk 6 haneye göre belirlenir.
/// </summary>
[DataContract]
public enum CardBrand
{
    /// <summary>Visa kart ağı (BIN: 4 ile başlar)</summary>
    [EnumMember]
    Visa = 1,

    /// <summary>Mastercard kart ağı (BIN: 51-55, 2221-2720 ile başlar)</summary>
    [EnumMember]
    Mastercard = 2,

    /// <summary>Troy — Türkiye'nin Ödeme Yöntemi (BKM tarafından işletilen yerli kart ağı)</summary>
    [EnumMember]
    Troy = 3,

    /// <summary>American Express (BIN: 34, 37 ile başlar)</summary>
    [EnumMember]
    AmericanExpress = 4
}
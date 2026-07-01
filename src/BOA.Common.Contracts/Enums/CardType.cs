using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kart tiplerini belirten numaralandırmadır (Enum).
/// WCF üzerinde taşınabilmesi için [DataContract] ve elemanları için [EnumMember] kullanılmıştır.
/// </summary>
[DataContract]
public enum CardType
{
    /// <summary>
    /// Banka Kartı (Debit Card) - Müşterinin vadesiz hesabına bağlı çalışır.
    /// </summary>
    [EnumMember]
    Debit = 1,

    /// <summary>
    /// Kredi Kartı (Credit Card) - Bankanın tanımladığı limit dahilinde harcama yaptırır.
    /// </summary>
    [EnumMember]
    Credit = 2
}

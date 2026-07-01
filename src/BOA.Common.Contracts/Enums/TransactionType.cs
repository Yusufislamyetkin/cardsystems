using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kart hareketlerinin (işlemlerinin) türünü belirten numaralandırmadır.
/// </summary>
[DataContract]
public enum TransactionType
{
    /// <summary>
    /// Alışveriş (POS/E-Ticaret Harcaması)
    /// </summary>
    [EnumMember]
    Purchase = 1,

    /// <summary>
    /// ATM'den Nakit Çekim
    /// </summary>
    [EnumMember]
    Withdrawal = 2,

    /// <summary>
    /// ATM'den veya hesaptan Karta/Hesaba Para Yatırma
    /// </summary>
    [EnumMember]
    Deposit = 3,

    /// <summary>
    /// Kart Üyelik Ücreti veya Faiz/Kâr Payı/Komisyon Yansıtılması
    /// </summary>
    [EnumMember]
    Fee = 4,

    /// <summary>
    /// Önceki bir harcamanın kısmen veya tamamen iadesi (Chargeback/Refund).
    /// </summary>
    [EnumMember]
    Refund = 5,

    /// <summary>
    /// Provizyonu alınmış ancak tamamlanmamış bir işlemin iptali (Reversal/Void).
    /// </summary>
    [EnumMember]
    Reversal = 6
}

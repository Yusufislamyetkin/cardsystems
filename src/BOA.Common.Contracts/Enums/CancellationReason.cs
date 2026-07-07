using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kart iptal nedenini temsil eden numaralandırmadır.
/// </summary>
[DataContract]
public enum CancellationReason
{
    /// <summary>
    /// Müşteri talebiyle iptal.
    /// </summary>
    [EnumMember]
    CustomerRequest = 1,

    /// <summary>
    /// Dolandırıcılık teyitli iptal.
    /// </summary>
    [EnumMember]
    FraudConfirmed = 2,

    /// <summary>
    /// Kart sahibi vefatı.
    /// </summary>
    [EnumMember]
    DeathOfHolder = 3,

    /// <summary>
    /// Hesap kapanışı.
    /// </summary>
    [EnumMember]
    AccountClosure = 4,

    /// <summary>
    /// Borç terkin/zarar yazma.
    /// </summary>
    [EnumMember]
    DebtWriteOff = 5,

    /// <summary>
    /// Yenileme/yeniden basım nedeniyle eski kart iptali.
    /// </summary>
    [EnumMember]
    CardReplaced = 6,

    /// <summary>
    /// Banka kararı (risk, uyumsuzluk vb.).
    /// </summary>
    [EnumMember]
    BankDecision = 7
}
using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kart bloke etme nedenini temsil eden numaralandırmadır.
/// ISO 8583 DE39 mantığıyla yapısal olarak saklanır.
/// </summary>
[DataContract]
public enum BlockReason
{
    /// <summary>
    /// Kayıp kart bildirimi.
    /// </summary>
    [EnumMember]
    LostCard = 1,

    /// <summary>
    /// Çalıntı kart bildirimi.
    /// </summary>
    [EnumMember]
    StolenCard = 2,

    /// <summary>
    /// Fraud/dolandırıcılık şüphesi.
    /// </summary>
    [EnumMember]
    FraudSuspicion = 3,

    /// <summary>
    /// Müşteri talebiyle geçici bloke.
    /// </summary>
    [EnumMember]
    CustomerRequest = 4,

    /// <summary>
    /// Borç gecikme (otomatik EOD blokajı).
    /// </summary>
    [EnumMember]
    DebtOverdue = 5,

    /// <summary>
    /// Mahkeme/icra kararı.
    /// </summary>
    [EnumMember]
    CourtOrder = 6
}
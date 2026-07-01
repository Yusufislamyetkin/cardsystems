using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Dtos;

/// <summary>
/// Kredi kartı hesap kesimi (ekstre) kaydını temsil eden Veri Transfer Nesnesidir.
/// </summary>
[DataContract]
public class StatementDto
{
    [DataMember]
    public int StatementId { get; set; }

    [DataMember]
    public int CardId { get; set; }

    [DataMember]
    public DateTime StatementDate { get; set; }

    [DataMember]
    public DateTime DueDate { get; set; }

    [DataMember]
    public decimal TotalDebt { get; set; }

    /// <summary>
    /// BDDK'nin kademeli asgari ödeme oranlarının basitleştirilmiş bir yaklaşımıdır (sabit %20).
    /// Gerçek oranlar kart limitine göre kademeli olarak değişir; bu proje bunu modellemez.
    /// </summary>
    [DataMember]
    public decimal MinimumPayment { get; set; }

    [DataMember]
    public bool IsPaid { get; set; }

    [DataMember]
    public bool InterestApplied { get; set; }

    [DataMember]
    public DateTime CreatedDate { get; set; }
}

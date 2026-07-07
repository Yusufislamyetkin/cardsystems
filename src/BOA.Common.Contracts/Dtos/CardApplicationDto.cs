using System.Runtime.Serialization;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Dtos;

/// <summary>
/// Kredi kartı başvuru akışındaki (başvuru → skorlama → onay → basım) bir kaydı taşıyan DTO'dur.
/// </summary>
[DataContract]
public class CardApplicationDto
{
    [DataMember]
    public int ApplicationId { get; set; }

    [DataMember]
    public string NationalId { get; set; } = string.Empty;

    [DataMember]
    public string ApplicantName { get; set; } = string.Empty;

    [DataMember]
    public string? Phone { get; set; }

    [DataMember]
    public decimal DeclaredMonthlyIncome { get; set; }

    [DataMember]
    public decimal RequestedLimit { get; set; }

    [DataMember]
    public int CreditScore { get; set; }

    [DataMember]
    public decimal BddkLimitCap { get; set; }

    [DataMember]
    public decimal? ApprovedLimit { get; set; }

    [DataMember]
    public CardApplicationStatus Status { get; set; }

    [DataMember]
    public string? DecisionReason { get; set; }

    [DataMember]
    public string MakerUserId { get; set; } = string.Empty;

    [DataMember]
    public string? CheckerUserId { get; set; }

    [DataMember]
    public int? CardId { get; set; }

    [DataMember]
    public DateTime CreatedDate { get; set; }

    [DataMember]
    public DateTime? DecidedDate { get; set; }

    [DataMember]
    public DateTime? IssuedDate { get; set; }
}

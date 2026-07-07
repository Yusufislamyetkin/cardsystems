using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Yeni kredi kartı başvurusu girişi (gişe personeli). Karar motoru başvuru anında senkron çalışır.
/// </summary>
[DataContract]
public class ApplyForCreditCardRequest : RequestBase
{
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
}

[DataContract]
public class ApplyForCreditCardResponse : ResponseBase
{
    [DataMember]
    public CardApplicationDto? Application { get; set; }
}

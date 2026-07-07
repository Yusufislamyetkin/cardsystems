using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Manuel değerlendirme kuyruğundaki bir başvuruyu onaylamak veya reddetmek için kullanılır.
/// Four-eyes: kararı veren kullanıcı başvuruyu giren (maker) ile aynı olamaz.
/// </summary>
[DataContract]
public class DecideCardApplicationRequest : RequestBase
{
    [DataMember]
    public int ApplicationId { get; set; }

    [DataMember]
    public bool Approve { get; set; }

    /// <summary>Onayda kullanılacak limit; null ise min(istenen, BDDK tavanı) uygulanır.</summary>
    [DataMember]
    public decimal? ApprovedLimit { get; set; }

    [DataMember]
    public string? DecisionNote { get; set; }
}

[DataContract]
public class DecideCardApplicationResponse : ResponseBase
{
    [DataMember]
    public CardApplicationDto? Application { get; set; }
}

[DataContract]
public class GetCardApplicationsRequest : RequestBase
{
    [DataMember]
    public CardApplicationStatus? Status { get; set; }

    [DataMember]
    public string? NationalId { get; set; }

    /// <summary>true: yalnızca açık başvurular (ManualReview, AutoApproved, Approved).</summary>
    [DataMember]
    public bool OnlyOpen { get; set; }
}

[DataContract]
public class GetCardApplicationsResponse : ResponseBase
{
    [DataMember]
    public List<CardApplicationDto> Applications { get; set; } = new();
}

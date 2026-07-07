using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Basılmış ancak henüz aktive edilmemiş (PendingActivation) bir kartı kimlik + PIN ile aktive eder.
/// </summary>
[DataContract]
public class ActivateCardRequest : RequestBase
{
    [DataMember]
    public int CardId { get; set; }

    [DataMember]
    public string NationalId { get; set; } = string.Empty;

    [DataMember]
    public string Pin { get; set; } = string.Empty;
}

[DataContract]
public class ActivateCardResponse : ResponseBase
{
    [DataMember]
    public CardDto? ActivatedCard { get; set; }
}

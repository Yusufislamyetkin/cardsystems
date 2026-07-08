using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

[DataContract]
public class CreateTemporaryLimitIncreaseRequest : RequestBase
{
    [DataMember] public int CardId { get; set; }
    [DataMember] public decimal TemporaryLimit { get; set; }
    [DataMember] public int DurationDays { get; set; }
    [DataMember] public string Reason { get; set; } = string.Empty;
}

[DataContract]
public class CreateTemporaryLimitIncreaseResponse : ResponseBase
{
    [DataMember] public TemporaryLimitDto TemporaryLimit { get; set; }
    [DataMember] public CardDto UpdatedCard { get; set; }
}

[DataContract]
public class RevertTemporaryLimitRequest : RequestBase
{
    [DataMember] public int CardId { get; set; }
    [DataMember] public string Reason { get; set; } = string.Empty;
}

[DataContract]
public class RevertTemporaryLimitResponse : ResponseBase
{
    [DataMember] public CardDto UpdatedCard { get; set; }
}
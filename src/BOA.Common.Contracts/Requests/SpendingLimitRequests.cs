using System.Runtime.Serialization;
using System.Collections.Generic;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

[DataContract]
public class UpdateSpendingLimitRequest : RequestBase
{
    [DataMember] public int CardId { get; set; }
    [DataMember] public SpendingLimitType LimitType { get; set; }
    [DataMember] public decimal NewAmount { get; set; }
}

[DataContract]
public class UpdateSpendingLimitResponse : ResponseBase
{
    [DataMember] public SpendingLimitDto UpdatedLimit { get; set; }
}

[DataContract]
public class GetSpendingLimitsRequest : RequestBase
{
    [DataMember] public int CardId { get; set; }
}

[DataContract]
public class GetSpendingLimitsResponse : ResponseBase
{
    [DataMember] public List<SpendingLimitDto> Limits { get; set; } = new();
}
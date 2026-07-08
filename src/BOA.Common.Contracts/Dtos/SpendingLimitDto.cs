using System.Runtime.Serialization;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Dtos;

[DataContract]
public class SpendingLimitDto
{
    [DataMember] public int SpendingLimitId { get; set; }
    [DataMember] public int CardId { get; set; }
    [DataMember] public SpendingLimitType LimitType { get; set; }
    [DataMember] public decimal LimitAmount { get; set; }
    [DataMember] public decimal UsedToday { get; set; }
    [DataMember] public decimal UsedThisMonth { get; set; }
    [DataMember] public DateTime LastResetDate { get; set; }
}
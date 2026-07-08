using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

[DataContract]
public enum SpendingLimitType
{
    [EnumMember] DailyAtmWithdrawal = 1,
    [EnumMember] DailyPosSpending = 2,
    [EnumMember] MonthlyTotalSpending = 3
}
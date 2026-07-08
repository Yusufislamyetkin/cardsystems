using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

[DataContract]
public enum SubLimitType { [EnumMember] CashAdvance = 1, [EnumMember] Installment = 2 }
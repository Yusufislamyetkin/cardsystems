using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Limit değişikliğinin türünü temsil eden numaralandırmadır.
/// </summary>
[DataContract]
public enum LimitChangeType
{
    [EnumMember] PermanentIncrease = 1,
    [EnumMember] PermanentDecrease = 2,
    [EnumMember] TemporaryIncrease = 3,
    [EnumMember] TemporaryRevert = 4
}
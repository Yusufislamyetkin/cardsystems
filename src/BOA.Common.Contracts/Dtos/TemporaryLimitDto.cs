using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Dtos;

[DataContract]
public class TemporaryLimitDto
{
    [DataMember] public int TempLimitId { get; set; }
    [DataMember] public int CardId { get; set; }
    [DataMember] public decimal OriginalLimit { get; set; }
    [DataMember] public decimal TemporaryLimit { get; set; }
    [DataMember] public DateTime StartDate { get; set; }
    [DataMember] public DateTime ExpiryDate { get; set; }
    [DataMember] public bool IsActive { get; set; }
    [DataMember] public string Reason { get; set; } = string.Empty;
    [DataMember] public string CreatedByUserId { get; set; } = string.Empty;
    [DataMember] public DateTime CreatedDate { get; set; }
    [DataMember] public DateTime? RevertedDate { get; set; }
}
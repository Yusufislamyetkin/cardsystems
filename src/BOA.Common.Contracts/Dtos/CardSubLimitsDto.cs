using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Dtos;

[DataContract]
public class CardSubLimitsDto
{
    [DataMember] public int CardId { get; set; }
    [DataMember] public decimal TotalLimit { get; set; }
    [DataMember] public decimal CashAdvanceLimit { get; set; }
    [DataMember] public decimal InstallmentLimit { get; set; }
    [DataMember] public decimal CashAdvanceUsed { get; set; }
    [DataMember] public decimal InstallmentUsed { get; set; }
}
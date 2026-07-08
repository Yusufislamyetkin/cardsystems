using System.Runtime.Serialization;
using System.Collections.Generic;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

[DataContract]
public class CreateInstallmentTransactionRequest : RequestBase
{
    [DataMember] public int CardId { get; set; }
    [DataMember] public decimal TotalAmount { get; set; }
    [DataMember] public int InstallmentCount { get; set; }
    [DataMember] public string Description { get; set; } = string.Empty;
    [DataMember] public string Pin { get; set; } = string.Empty;
    [DataMember] public string? MerchantId { get; set; }
    [DataMember] public string? Mcc { get; set; }
}

[DataContract]
public class CreateInstallmentTransactionResponse : ResponseBase
{
    [DataMember] public List<TransactionDto> InstallmentEntries { get; set; } = new();
    [DataMember] public CardDto UpdatedCard { get; set; }
}
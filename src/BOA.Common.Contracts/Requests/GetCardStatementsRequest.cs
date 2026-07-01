using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

[DataContract]
public class GetCardStatementsRequest : RequestBase
{
    [DataMember]
    public int CardId { get; set; }
}

[DataContract]
public class GetCardStatementsResponse : ResponseBase
{
    [DataMember]
    public List<StatementDto> Statements { get; set; } = new();
}

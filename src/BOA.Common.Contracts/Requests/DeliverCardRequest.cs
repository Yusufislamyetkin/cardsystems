using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Basılmış kartın şubede müşteriye teslim edildiğini kaydeder.
/// Kart InTransit durumundan PendingActivation'a geçer; müşteri daha sonra ActivateCard ile aktive eder.
/// </summary>
[DataContract]
public class DeliverCardRequest : RequestBase
{
    [DataMember]
    public int CardId { get; set; }

    [DataMember]
    public string? TrackingNumber { get; set; }
}

[DataContract]
public class DeliverCardResponse : ResponseBase
{
    [DataMember]
    public CardDto? DeliveredCard { get; set; }
}
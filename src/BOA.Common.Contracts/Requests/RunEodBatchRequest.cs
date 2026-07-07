using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Gün sonu (End of Day) batch sürecini tetikleyen istektir. Gerçek bir bankada bu süreç
/// gece yarısı zamanlanmış bir iş (Hangfire/Quartz.NET) olarak otomatik çalışır; bu projede
/// eğitim/demo amaçlı olarak manuel tetiklenebilir bir uç nokta olarak sunulur.
/// </summary>
[DataContract]
public class RunEodBatchRequest : RequestBase
{
}

[DataContract]
public class RunEodBatchResponse : ResponseBase
{
    [DataMember]
    public int StatementsGenerated { get; set; }

    [DataMember]
    public int InterestAppliedCount { get; set; }

    [DataMember]
    public int CardsAutoBlocked { get; set; }

    [DataMember]
    public int CardsRenewed { get; set; }

    [DataMember]
    public int CardsIssued { get; set; }

    [DataMember]
    public int ApplicationsExpired { get; set; }

    [DataMember]
    public int ReplacementCardsIssued { get; set; }
}

using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Bankanın kendi tarafında kayıtlı ama PayCore ile senkronu belirsiz kalmış (PayCore'a hiç
/// ulaşılamamış, veya süreç PayCore'u aramadan önce çökmüş) işlemleri tarayıp yeniden dener.
/// Gerçek bir bankada bu, gün içinde periyodik çalışan bir mutabakat (reconciliation) işidir;
/// bu projede eğitim/demo amaçlı manuel tetiklenebilir bir uç nokta olarak sunulur.
/// </summary>
[DataContract]
public class RetryPendingPaycoreSyncsRequest : RequestBase
{
}

[DataContract]
public class RetryPendingPaycoreSyncsResponse : ResponseBase
{
    [DataMember]
    public int Confirmed { get; set; }

    [DataMember]
    public int Declined { get; set; }

    [DataMember]
    public int StillUnreachable { get; set; }
}

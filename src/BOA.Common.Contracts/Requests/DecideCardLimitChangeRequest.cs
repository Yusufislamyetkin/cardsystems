using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Onay bekleyen bir limit artış talebini karara bağlamak (onay/red) için kullanılır.
/// Maker-checker kuralı: kararı veren kullanıcı (UserId), talebi giren kullanıcı (maker) ile
/// AYNI olamaz — aynı kişinin kendi talebini onaylaması engellenir (four-eyes principle).
/// </summary>
[DataContract]
public class DecideCardLimitChangeRequest : RequestBase
{
    /// <summary>Karara bağlanacak limit artış talebinin ID'si.</summary>
    [DataMember]
    public int LimitRequestId { get; set; }

    /// <summary>true: talebi onayla ve yeni limiti uygula; false: talebi reddet.</summary>
    [DataMember]
    public bool Approve { get; set; }

    /// <summary>Checker'ın karar notu (özellikle redlerde beklenir).</summary>
    [DataMember]
    public string? DecisionNote { get; set; }
}

/// <summary>
/// Limit artış talebi kararının yanıtıdır.
/// </summary>
[DataContract]
public class DecideCardLimitChangeResponse : ResponseBase
{
    /// <summary>Karara bağlanmış talebin güncel hali (Approved veya Rejected).</summary>
    [DataMember]
    public LimitChangeRequestDto? DecidedRequest { get; set; }

    /// <summary>Onay verildiyse, yeni limiti uygulanmış kartın güncel hali; redde null olabilir.</summary>
    [DataMember]
    public CardDto? UpdatedCard { get; set; }
}

/// <summary>
/// Limit artış taleplerini (varsayılan olarak yalnızca onay bekleyenleri) listelemek için kullanılır.
/// </summary>
[DataContract]
public class GetLimitChangeRequestsRequest : RequestBase
{
    /// <summary>Verilirse yalnızca bu karta ait talepler listelenir.</summary>
    [DataMember]
    public int? CardId { get; set; }

    /// <summary>true (varsayılan): yalnızca PendingApproval; false: tüm talepler.</summary>
    [DataMember]
    public bool OnlyPending { get; set; } = true;
}

/// <summary>
/// Limit artış talepleri listesinin yanıtıdır.
/// </summary>
[DataContract]
public class GetLimitChangeRequestsResponse : ResponseBase
{
    [DataMember]
    public List<LimitChangeRequestDto> Requests { get; set; } = new();
}

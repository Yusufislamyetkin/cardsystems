using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Daha önce alınmış (Authorized durumundaki) bir provizyonu kesinleştirir (Capture):
/// yevmiye defterine gerçek borç/alacak kaydını yazar ve kart bakiyesini günceller.
/// </summary>
[DataContract]
public class CaptureAuthorizationRequest : RequestBase
{
    [DataMember]
    public int AuthorizationId { get; set; }
}

[DataContract]
public class CaptureAuthorizationResponse : ResponseBase
{
    [DataMember]
    public AuthorizationDto? Authorization { get; set; }

    [DataMember]
    public CardDto? UpdatedCard { get; set; }
}

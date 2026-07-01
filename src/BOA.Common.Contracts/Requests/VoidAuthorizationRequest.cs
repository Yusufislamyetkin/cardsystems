using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Daha önce alınmış (Authorized durumundaki) bir provizyonu iptal eder (Void):
/// yevmiye defterine hiçbir kayıt yazılmaz, bloke edilen tutar serbest bırakılır.
/// </summary>
[DataContract]
public class VoidAuthorizationRequest : RequestBase
{
    [DataMember]
    public int AuthorizationId { get; set; }

    [DataMember]
    public string Reason { get; set; } = string.Empty;
}

[DataContract]
public class VoidAuthorizationResponse : ResponseBase
{
    [DataMember]
    public AuthorizationDto? Authorization { get; set; }
}

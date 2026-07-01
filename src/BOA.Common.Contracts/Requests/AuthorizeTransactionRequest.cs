using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Bir işlem için provizyon (authorization/hold) talep etmek için kullanılan istektir.
/// Provizyon alınması, tutarı doğrudan yevmiye defterine yazmaz; yalnızca kullanılabilir
/// bakiye/limit üzerinde bir bloke oluşturur. Kesinleşme için ayrıca Capture çağrılmalıdır.
/// </summary>
[DataContract]
public class AuthorizeTransactionRequest : RequestBase
{
    [DataMember]
    public int CardId { get; set; }

    [DataMember]
    public TransactionType TransactionType { get; set; }

    [DataMember]
    public decimal Amount { get; set; }

    [DataMember]
    public string Description { get; set; } = string.Empty;

    [DataMember]
    public string Pin { get; set; } = string.Empty;

    [DataMember]
    public string? MerchantId { get; set; }

    [DataMember]
    public string? Mcc { get; set; }
}

[DataContract]
public class AuthorizeTransactionResponse : ResponseBase
{
    /// <summary>
    /// Oluşan provizyon kaydı (Approved/Declined durumundan bağımsız — reddedilen provizyonlar da kayıt altına alınır).
    /// </summary>
    [DataMember]
    public AuthorizationDto? Authorization { get; set; }
}

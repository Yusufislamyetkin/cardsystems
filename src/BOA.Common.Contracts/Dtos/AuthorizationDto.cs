using System.Runtime.Serialization;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Dtos;

/// <summary>
/// Bir provizyon (authorization) kaydını temsil eden Veri Transfer Nesnesidir.
/// </summary>
[DataContract]
public class AuthorizationDto
{
    [DataMember]
    public int AuthorizationId { get; set; }

    [DataMember]
    public int CardId { get; set; }

    [DataMember]
    public TransactionType TransactionType { get; set; }

    [DataMember]
    public decimal Amount { get; set; }

    /// <summary>
    /// ISO 8583 DE39 karşılığı yanıt kodu.
    /// </summary>
    [DataMember]
    public AuthResponseCode ResponseCode { get; set; }

    /// <summary>
    /// Onaylanan provizyonlar için üretilen 6 haneli alfanümerik provizyon (yetki) kodu.
    /// Reddedilen provizyonlarda boş olur.
    /// </summary>
    [DataMember]
    public string? AuthorizationCode { get; set; }

    [DataMember]
    public AuthorizationStatus Status { get; set; }

    [DataMember]
    public string Description { get; set; } = string.Empty;

    [DataMember]
    public string ReferenceNumber { get; set; } = string.Empty;

    [DataMember]
    public string? MerchantId { get; set; }

    [DataMember]
    public string? Mcc { get; set; }

    [DataMember]
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// Bu provizyonun PayCore tarafındaki karşılığının referansı (bankanın kendi onayından SONRA,
    /// PayCore'a gönderilen provizyon isteği de onaylanırsa doldurulur).
    /// </summary>
    [DataMember]
    public string? PaycoreAuthReference { get; set; }
}

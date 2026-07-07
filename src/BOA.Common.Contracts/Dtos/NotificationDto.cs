using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Dtos;

/// <summary>
/// Müşteri bildirimi (SMS/e-posta) kaydı — bankacılık sürecindeki önemli olaylarda
/// müşteriye gönderilen bildirimlerin denetim izi.
/// </summary>
[DataContract]
public class NotificationDto
{
    [DataMember]
    public int NotificationId { get; set; }

    [DataMember]
    public string NationalId { get; set; } = string.Empty;

    [DataMember]
    public string Channel { get; set; } = string.Empty;

    [DataMember]
    public string TemplateKey { get; set; } = string.Empty;

    [DataMember]
    public string Message { get; set; } = string.Empty;

    [DataMember]
    public DateTime CreatedDate { get; set; }
}
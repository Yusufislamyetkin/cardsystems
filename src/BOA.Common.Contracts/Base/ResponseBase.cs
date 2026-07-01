using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Base;

/// <summary>
/// BOA mimarisinde tüm servis yanıt (Response) sınıflarının kalıtım alması gereken temel sınıftır.
/// Servis çağrısının başarısı, hata detayları ve varsa uyarı mesajları bu sınıf aracılığıyla döner.
/// </summary>
[DataContract]
public class ResponseBase
{
    /// <summary>
    /// İşlemin başarıyla tamamlanıp tamamlanmadığını belirtir.
    /// UI ve istemci uygulamalar öncelikle bu değere bakarak akışı yönlendirir.
    /// </summary>
    [DataMember]
    public bool IsSuccess { get; set; } = true;

    /// <summary>
    /// Eğer işlem başarısız ise hata kodunu taşır (Örn: "ERR-CARD-001", "VAL-LIMIT-EXCEEDED").
    /// Boş olması, hatanın olmadığını gösterir.
    /// </summary>
    [DataMember]
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Kullanıcıya veya log sistemine gösterilmek üzere oluşturulmuş açıklayıcı hata mesajıdır.
    /// </summary>
    [DataMember]
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// İşlem başarıyla tamamlandığında dönecek bilgilendirme mesajıdır (Örn: "İşlem Başarıyla Gerçekleşti").
    /// </summary>
    [DataMember]
    public string ResultMessage { get; set; } = string.Empty;

    /// <summary>
    /// Servisin çalışma süresini (milisaniye cinsinden) tutar. Performance monitoring için yararlıdır.
    /// </summary>
    [DataMember]
    public long ExecutionTimeMs { get; set; }

    /// <summary>
    /// Servisin çalıştığı sunucu adı. Sorun giderme (troubleshooting) için hangi node'un çalıştığını gösterir.
    /// </summary>
    [DataMember]
    public string MachineName { get; set; } = Environment.MachineName;
}

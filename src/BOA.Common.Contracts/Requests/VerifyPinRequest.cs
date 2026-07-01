using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Kart şifresinin (PIN) HSM üzerinden doğrulanması için gönderilen parametreleri sarmalayan sınıftır.
/// </summary>
[DataContract]
public class VerifyPinRequest : RequestBase
{
    /// <summary>
    /// Şifresi doğrulanacak kartın ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// Doğrulanacak 4 haneli ham şifre.
    /// </summary>
    [DataMember]
    public string Pin { get; set; } = string.Empty;
}

/// <summary>
/// Şifre doğrulama işlemi sonucunda dönen yanıt sınıfıdır.
/// </summary>
[DataContract]
public class VerifyPinResponse : ResponseBase
{
    /// <summary>
    /// Şifrenin doğru olup olmadığı bilgisi.
    /// </summary>
    [DataMember]
    public bool IsPinValid { get; set; }
}

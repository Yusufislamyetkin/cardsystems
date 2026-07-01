using System;
using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Base;

/// <summary>
/// Kurumsal WCF SOAP servislerinde hata iletimi (Fault Contract) için kullanılan standart modeldir.
/// Sistem içindeki teknik hataları ve iş mantığı (business) ihlallerini sarmalayarak 
/// istemciye güvenli, temiz ve standart bir formatta hata kodu iletmeyi sağlar.
/// </summary>
[DataContract(Namespace = "http://emlakkatilim.com.tr/boa/card/fault")]
public class BankingFault
{
    /// <summary>
    /// Hata kodu (Örn: ACCESS_DENIED, INSUFFICIENT_LIMIT, INVALID_PARAMETER)
    /// </summary>
    [DataMember]
    public string ErrorCode { get; set; } = string.Empty;

    /// <summary>
    /// Kullanıcıya veya istemciye gösterilecek açıklayıcı hata mesajı
    /// </summary>
    [DataMember]
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// Hatanın gerçekleştiği zaman damgası
    /// </summary>
    [DataMember]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Hatanın önem derecesi (Error, Warning, Critical)
    /// </summary>
    [DataMember]
    public string Severity { get; set; } = "Error";
}

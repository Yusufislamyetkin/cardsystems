using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Base;

/// <summary>
/// BOA mimarisinde tüm servis istek (Request) sınıflarının kalıtım alması gereken temel sınıftır.
/// Bu sınıf, isteği yapan kanal, kullanıcı, şube gibi denetim (audit) ve izleme (logging) bilgilerini taşır.
/// WCF üzerinde serileştirilebilmesi için [DataContract] özniteliği ile işaretlenmiştir.
/// </summary>
[DataContract]
public class RequestBase
{
    /// <summary>
    /// İstekte bulunan kanal bilgisi (Örn: "MOBILE", "INTERNET", "ATM", "BRANCH").
    /// BOA altyapısında yetkilendirme ve akış limitleri kanala göre değişiklik gösterebilir.
    /// </summary>
    [DataMember]
    public string Channel { get; set; } = "WEB";

    /// <summary>
    /// İşlemi gerçekleştiren kullanıcının benzersiz kimliği (Sicil No / User ID).
    /// Bankacılık mevzuatı gereği her işlemin kimin tarafından yapıldığının loglanması zorunludur.
    /// </summary>
    [DataMember]
    public string UserId { get; set; } = "SYSTEM";

    /// <summary>
    /// İşlemin yapıldığı şube kodu (Branch ID).
    /// Muhasebe kayıtlarının hangi şube üzerinden geçeceğinin tespiti için önemlidir.
    /// </summary>
    [DataMember]
    public int BranchId { get; set; } = 999; // 999 genellikle sistem/merkez şubeyi temsil eder.

    /// <summary>
    /// İstemcinin IP adresi (IP Address).
    /// Güvenlik denetimleri ve dolandırıcılık (fraud) engelleme sistemleri için loglanır.
    /// </summary>
    [DataMember]
    public string ClientIp { get; set; } = "127.0.0.1";

    /// <summary>
    /// İstemcinin dil tercihi (Örn: "tr-TR", "en-US").
    /// Veritabanından dönecek hata/uyarı mesajlarının yerelleştirilmesi (localization) için kullanılır.
    /// </summary>
    [DataMember]
    public string Language { get; set; } = "tr-TR";

    /// <summary>
    /// İşlemin yapıldığı tarih ve saat bilgisi.
    /// </summary>
    [DataMember]
    public DateTime RequestTime { get; set; } = DateTime.Now;
}

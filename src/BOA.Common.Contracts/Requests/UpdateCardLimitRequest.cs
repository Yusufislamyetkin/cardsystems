using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Bir kartın limitini güncellemek için gönderilecek parametreleri içeren sınıftır.
/// Limit DÜŞÜŞÜ anında uygulanır; limit ARTIŞI ise maker-checker (çift onay) akışına girer:
/// bu istek yalnızca bir onay talebi (PendingApproval) oluşturur, limit ancak FARKLI bir
/// yetkilinin DecideCardLimitChange ile onayı sonrasında karta yansır.
/// </summary>
[DataContract]
public class UpdateCardLimitRequest : RequestBase
{
    /// <summary>
    /// Limiti güncellenecek kartın sistemdeki benzersiz ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// Kartın tanımlanması istenen yeni limit değeri.
    /// Negatif olamaz.
    /// </summary>
    [DataMember]
    public decimal NewLimit { get; set; }

    /// <summary>
    /// Limit değişikliğinin iş gerekçesi (müşteri talebi, risk kararı vb.). Zorunludur;
    /// denetim (audit) kaydına işlenir. Gerçek bankada gerekçesiz limit değişikliği yapılamaz.
    /// </summary>
    [DataMember]
    public string Reason { get; set; } = string.Empty;
    [DataMember] public decimal? NewCashAdvanceLimit { get; set; }
    [DataMember] public decimal? NewInstallmentLimit { get; set; }
}

/// <summary>
/// Limit güncelleme işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class UpdateCardLimitResponse : ResponseBase
{
    /// <summary>
    /// Limit güncelleme sonrası güncel kart detayları. Limit artışı talebinde limit HENÜZ
    /// değişmediği için buradaki kart, mevcut (değişmemiş) limiti taşır.
    /// </summary>
    [DataMember]
    public CardDto UpdatedCard { get; set; }

    /// <summary>
    /// Limit artışı talep edildiyse oluşturulan onay bekleyen maker-checker talebi; düşüşlerde null.
    /// </summary>
    [DataMember]
    public LimitChangeRequestDto? PendingRequest { get; set; }

    /// <summary>
    /// Yeni limit, kartın mevcut borcunun altına düşürüldüyse true olur. İşlem yine kabul edilir
    /// (gerçek bankada da limit borç altına indirilebilir) ancak kart "limit aşımı" durumundadır.
    /// </summary>
    [DataMember]
    public bool IsOverlimit { get; set; }
}

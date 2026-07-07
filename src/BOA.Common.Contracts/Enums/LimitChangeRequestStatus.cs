using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Bir kredi kartı limit ARTIŞ talebinin maker-checker (çift onay) yaşam döngüsündeki durumunu belirtir.
/// Gerçek bankalarda limit artışı tek kişinin girip uygulayabileceği bir işlem değildir: bir kullanıcı
/// (maker) talebi girer, FARKLI bir yetkili (checker) onaylar/reddeder. Limit düşüşleri risk azalttığı
/// için bu akışa tabi değildir ve anında uygulanır.
/// </summary>
[DataContract]
public enum LimitChangeRequestStatus
{
    /// <summary>Talep girildi, ikinci bir yetkilinin (checker) kararını bekliyor.</summary>
    [EnumMember]
    PendingApproval = 1,

    /// <summary>Talep farklı bir yetkili tarafından onaylandı ve yeni limit karta uygulandı.</summary>
    [EnumMember]
    Approved = 2,

    /// <summary>Talep reddedildi; kartın limiti değişmedi.</summary>
    [EnumMember]
    Rejected = 3
}

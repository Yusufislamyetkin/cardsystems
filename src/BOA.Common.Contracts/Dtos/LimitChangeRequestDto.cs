using System.Runtime.Serialization;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Dtos;

/// <summary>
/// Maker-checker (çift onay) akışındaki bir kredi kartı limit artış talebini taşıyan DTO'dur.
/// Talebi giren kullanıcı (maker) ile karara bağlayan kullanıcı (checker) ayrı ayrı izlenir;
/// aynı kullanıcının kendi talebini onaylaması (four-eyes ihlali) sistemce engellenir.
/// </summary>
[DataContract]
public class LimitChangeRequestDto
{
    /// <summary>Talebin sistemdeki benzersiz anahtarı.</summary>
    [DataMember]
    public int LimitRequestId { get; set; }

    /// <summary>Limiti artırılmak istenen kartın ID'si.</summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>Talep girildiği andaki mevcut limit.</summary>
    [DataMember]
    public decimal CurrentLimit { get; set; }

    /// <summary>Talep edilen yeni limit.</summary>
    [DataMember]
    public decimal RequestedLimit { get; set; }

    /// <summary>Talebin maker-checker yaşam döngüsündeki durumu.</summary>
    [DataMember]
    public LimitChangeRequestStatus Status { get; set; }

    /// <summary>Talebin iş gerekçesi (müşteri talebi, gelir güncellemesi vb.).</summary>
    [DataMember]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Talebi giren kullanıcı (maker).</summary>
    [DataMember]
    public string MakerUserId { get; set; } = string.Empty;

    /// <summary>Talebi karara bağlayan kullanıcı (checker); henüz karar verilmediyse null.</summary>
    [DataMember]
    public string? CheckerUserId { get; set; }

    /// <summary>Checker'ın karar notu (onay/red açıklaması).</summary>
    [DataMember]
    public string? DecisionNote { get; set; }

    /// <summary>Talebin girildiği tarih.</summary>
    [DataMember]
    public DateTime CreatedDate { get; set; }

    /// <summary>Kararın verildiği tarih; henüz karar verilmediyse null.</summary>
    [DataMember]
    public DateTime? DecidedDate { get; set; }
}

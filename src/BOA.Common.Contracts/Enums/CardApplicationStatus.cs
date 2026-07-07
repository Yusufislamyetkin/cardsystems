using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Kredi kartı başvurusunun karar motoru ve underwriter yaşam döngüsündeki durumunu belirtir.
/// Başvuru girişinde skorlama senkron çalışır; onaylanan başvurular EOD batch ile basılır.
/// </summary>
[DataContract]
public enum CardApplicationStatus
{
    /// <summary>Orta skor bandı — underwriter kuyruğunda manuel değerlendirme bekliyor.</summary>
    [EnumMember]
    ManualReview = 1,

    /// <summary>Karar motoru anında onayladı — EOD basım bekliyor.</summary>
    [EnumMember]
    AutoApproved = 2,

    /// <summary>Underwriter onayladı — EOD basım bekliyor.</summary>
    [EnumMember]
    Approved = 3,

    /// <summary>Karar motoru reddetti.</summary>
    [EnumMember]
    AutoRejected = 4,

    /// <summary>Underwriter reddetti.</summary>
    [EnumMember]
    Rejected = 5,

    /// <summary>Kart basıldı (card_id dolu).</summary>
    [EnumMember]
    Issued = 6,

    /// <summary>Karar verilmeden zaman aşımına uğradı.</summary>
    [EnumMember]
    Expired = 7
}

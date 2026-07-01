using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// Bir provizyonun (authorization/hold) yaşam döngüsündeki durumunu belirtir.
/// </summary>
[DataContract]
public enum AuthorizationStatus
{
    /// <summary>Provizyon alınmış (tutar bloke edilmiş), henüz kesinleşmemiş (capture edilmemiş).</summary>
    [EnumMember]
    Authorized = 1,

    /// <summary>Provizyon kesinleştirilmiş (Capture) — yevmiye defterine gerçek işlem olarak yazılmış.</summary>
    [EnumMember]
    Captured = 2,

    /// <summary>Provizyon iptal edilmiş (Void) — bloke kaldırılmış, hiçbir muhasebe kaydı oluşmamış.</summary>
    [EnumMember]
    Voided = 3,

    /// <summary>Provizyon reddedilmiş (Approved dışında bir response code ile sonuçlanmış).</summary>
    [EnumMember]
    Declined = 4
}

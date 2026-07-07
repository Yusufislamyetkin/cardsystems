using System.Runtime.Serialization;

namespace BOA.Common.Contracts.Enums;

/// <summary>
/// ISO 8583 DE39 (Response Code) alanının bu projede kullanılan alt kümesidir.
/// Gerçek kart switch'leri (Paycore/BKM/Visa/Mastercard) provizyon sonucunu bu tür kodlarla iletir;
/// önceki sürümde provizyon süreci hiç yoktu, işlemler doğrudan bakiyeyi güncelliyordu.
/// </summary>
[DataContract]
public enum AuthResponseCode
{
    /// <summary>00 - Onaylandı</summary>
    [EnumMember]
    Approved = 0,

    /// <summary>05 - İşlem Reddedildi (Do Not Honor)</summary>
    [EnumMember]
    DoNotHonor = 5,

    /// <summary>14 - Geçersiz Kart</summary>
    [EnumMember]
    InvalidCard = 14,

    /// <summary>51 - Yetersiz Bakiye/Limit</summary>
    [EnumMember]
    InsufficientFunds = 51,

    /// <summary>54 - Süresi Dolmuş Kart</summary>
    [EnumMember]
    ExpiredCard = 54,

    /// <summary>55 - Hatalı Şifre (PIN)</summary>
    [EnumMember]
    IncorrectPin = 55,

    /// <summary>61 - Günlük Nakit Çekim Limiti Aşıldı (Exceeds Withdrawal Amount Limit)</summary>
    [EnumMember]
    ExceedsWithdrawalLimit = 61,

    /// <summary>96 - Sistem Hatası</summary>
    [EnumMember]
    SystemError = 96
}

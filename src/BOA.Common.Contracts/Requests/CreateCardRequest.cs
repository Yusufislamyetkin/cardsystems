using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Yeni bir kart oluşturmak (Kart Tanımlama) için gönderilecek parametreleri içeren sınıftır.
/// </summary>
[DataContract]
public class CreateCardRequest : RequestBase
{
    /// <summary>
    /// Kart hamilinin (sahibinin) adı soyadı.
    /// Boş geçilemez.
    /// </summary>
    [DataMember]
    public string CardHolderName { get; set; } = string.Empty;

    /// <summary>
    /// Kartın tipi (Debit - Banka Kartı, Credit - Kredi Kartı).
    /// </summary>
    [DataMember]
    public CardType CardType { get; set; }

    /// <summary>
    /// Kart için tanımlanacak başlangıç limiti.
    /// </summary>
    [DataMember]
    public decimal Limit { get; set; }

    /// <summary>
    /// Karta yatırılacak başlangıç bakiyesi (özellikle vadesiz mevduat hesabı simülasyonu için).
    /// </summary>
    [DataMember]
    public decimal InitialBalance { get; set; }

    /// <summary>
    /// Kart için tanımlanacak 4 haneli ilk şifre (HSM üzerinde PIN Block'a dönüştürülür).
    /// </summary>
    [DataMember]
    public string Pin { get; set; } = string.Empty;

    /// <summary>
    /// Kart hamilinin T.C. Kimlik Numarası. Kart, bu numarayla eşleşen müşteri kaydına
    /// (yoksa yeni oluşturulan bir müşteriye) ve onun vadesiz/kredi hesabına bağlanır.
    /// </summary>
    [DataMember]
    public string NationalId { get; set; } = string.Empty;

    /// <summary>
    /// Müşteri iletişim telefonu (yeni müşteri oluşturulacaksa kullanılır).
    /// </summary>
    [DataMember]
    public string? Phone { get; set; }
}

/// <summary>
/// Kart oluşturma işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class CreateCardResponse : ResponseBase
{
    /// <summary>
    /// Başarıyla oluşturulan kartın detaylı veri modeli.
    /// </summary>
    [DataMember]
    public CardDto CreatedCard { get; set; }
}

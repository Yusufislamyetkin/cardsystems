using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Simülasyon amacıyla kart üzerinden bir işlem (harcama, nakit çekme veya para yatırma)
/// gerçekleştirmek için kullanılacak parametreleri içeren sınıftır.
/// </summary>
[DataContract]
public class CreateTransactionRequest : RequestBase
{
    /// <summary>
    /// İşlemin gerçekleştirileceği kartın ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }

    /// <summary>
    /// İşlem Türü (Alışveriş, ATM Çekim, ATM Yatırma vb.).
    /// </summary>
    [DataMember]
    public TransactionType TransactionType { get; set; }

    /// <summary>
    /// İşlem Tutarı.
    /// Sıfırdan büyük olmalıdır.
    /// </summary>
    [DataMember]
    public decimal Amount { get; set; }

    /// <summary>
    /// İşlemle ilgili açıklama metni.
    /// </summary>
    [DataMember]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// İşlem için girilen 4 haneli kart şifresi (Yalnızca Harcama ve Para Çekmede zorunludur).
    /// </summary>
    [DataMember]
    public string Pin { get; set; } = string.Empty;

    /// <summary>
    /// İşlemin gerçekleştiği üye iş yerinin kimliği (POS/E-ticaret harcamalarında). Kart-içi işlemlerde boş bırakılabilir.
    /// </summary>
    [DataMember]
    public string? MerchantId { get; set; }

    /// <summary>
    /// Üye iş yeri kategori kodu (Merchant Category Code, ISO 8583 DE18).
    /// </summary>
    [DataMember]
    public string? Mcc { get; set; }
}

/// <summary>
/// İşlem simülasyonu sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class CreateTransactionResponse : ResponseBase
{
    /// <summary>
    /// Başarıyla oluşturulan işlemin detaylı veri modeli.
    /// </summary>
    [DataMember]
    public TransactionDto CreatedTransaction { get; set; }

    /// <summary>
    /// İşlem sonrası kartın güncel bakiyesi ve limit bilgilerini yansıtan güncel kart modeli.
    /// </summary>
    [DataMember]
    public CardDto UpdatedCard { get; set; }
}

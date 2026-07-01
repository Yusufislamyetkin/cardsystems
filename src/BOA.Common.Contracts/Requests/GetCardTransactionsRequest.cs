using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// Bir kartın hesap/harcama hareketlerini sorgulamak için kullanılacak parametreleri içeren sınıftır.
/// </summary>
[DataContract]
public class GetCardTransactionsRequest : RequestBase
{
    /// <summary>
    /// Hareketleri sorgulanacak kartın sistemdeki ID'si.
    /// </summary>
    [DataMember]
    public int CardId { get; set; }
}

/// <summary>
/// Kart hareketleri sorgulama işlemi sonucunda dönecek yanıt sınıfıdır.
/// </summary>
[DataContract]
public class GetCardTransactionsResponse : ResponseBase
{
    /// <summary>
    /// Karta ait hareketlerin listesidir.
    /// </summary>
    [DataMember]
    public List<TransactionDto> Transactions { get; set; } = new List<TransactionDto>();
}

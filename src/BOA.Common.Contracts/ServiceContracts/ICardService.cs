using CoreWCF;
using BOA.Common.Contracts.Requests;
using BOA.Common.Contracts.Base;

namespace BOA.Common.Contracts.ServiceContracts;

/// <summary>
/// BOA Temel Kart Operasyonlarını yöneten WCF Servis Sözleşmesidir (Service Contract).
/// İstemciler (Web, Mobil veya diğer entegre sistemler) bu arayüz aracılığıyla sunucuya bağlanır.
/// [ServiceContract] özniteliği, bu arayüzün bir WCF servisi olduğunu belirtir.
/// </summary>
[ServiceContract(Namespace = "http://emlakkatilim.com.tr/boa/card")]
public interface ICardService
{
    /// <summary>
    /// Sisteme yeni bir kart tanımlar (Debit veya Kredi Kartı).
    /// </summary>
    /// <param name="request">Kart oluşturma parametreleri</param>
    /// <returns>Oluşturulan kart bilgisi</returns>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CreateCardResponse CreateCard(CreateCardRequest request);

    /// <summary>
    /// Sistemde kayıtlı olan kartları filtrelere göre sorgular ve listeler.
    /// </summary>
    /// <param name="request">Filtre parametreleri</param>
    /// <returns>Kart listesi</returns>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetCardListResponse GetCardList(GetCardListRequest request);

    /// <summary>
    /// Belirtilen kartın limitini günceller.
    /// </summary>
    /// <param name="request">Kart ID ve yeni limit bilgileri</param>
    /// <returns>Güncellenmiş kart bilgisi</returns>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    UpdateCardLimitResponse UpdateCardLimit(UpdateCardLimitRequest request);

    /// <summary>
    /// Kartın durumunu (Aktif, Bloke, İptal) günceller ve denetim loguna kaydeder.
    /// </summary>
    /// <param name="request">Kart ID ve yeni durum</param>
    /// <returns>Güncellenmiş kart bilgisi</returns>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    SetCardStatusResponse SetCardStatus(SetCardStatusRequest request);

    /// <summary>
    /// Karta ait tüm işlem (harcama/yatırma/çekim) geçmişini listeler.
    /// </summary>
    /// <param name="request">Kart ID</param>
    /// <returns>İşlem hareketleri listesi</returns>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetCardTransactionsResponse GetCardTransactions(GetCardTransactionsRequest request);

    /// <summary>
    /// Simüle edilmiş bir kart harcaması veya para yatırma işlemi oluşturur.
    /// </summary>
    /// <param name="request">İşlem detayları</param>
    /// <returns>Oluşturulan işlem ve güncellenmiş kart bakiye/limit detayları</returns>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CreateTransactionResponse CreateTransaction(CreateTransactionRequest request);

    /// <summary>
    /// Kart şifresinin (PIN) HSM üzerinden doğruluğunu kontrol eder.
    /// </summary>
    /// <param name="request">Kart ID ve girilen PIN</param>
    /// <returns>Doğrulama sonucu</returns>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    VerifyPinResponse VerifyPin(VerifyPinRequest request);

    /// <summary>
    /// Bir işlem için provizyon (authorization/hold) alır. Tutarı doğrudan yevmiye defterine yazmaz;
    /// yalnızca kullanılabilir bakiye/limit üzerinde bir bloke oluşturur ve ISO 8583 DE39 benzeri
    /// bir yanıt kodu (Approved/DoNotHonor/InsufficientFunds/...) döner.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    AuthorizeTransactionResponse AuthorizeTransaction(AuthorizeTransactionRequest request);

    /// <summary>
    /// Daha önce alınmış bir provizyonu kesinleştirir (Capture): yevmiye defterine gerçek
    /// borç/alacak kaydını yazar ve kart bakiyesini/kullanılabilir limitini günceller.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CaptureAuthorizationResponse CaptureAuthorization(CaptureAuthorizationRequest request);

    /// <summary>
    /// Daha önce alınmış bir provizyonu iptal eder (Void): bloke edilen tutarı serbest bırakır,
    /// hiçbir muhasebe kaydı oluşturmaz.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    VoidAuthorizationResponse VoidAuthorization(VoidAuthorizationRequest request);

    /// <summary>
    /// Gün sonu (End of Day) batch sürecini çalıştırır: kredi kartları için hesap kesimi (ekstre) oluşturur,
    /// vadesi geçmiş ekstrelere gecikme faizi işler, ciddi gecikmelerde kartı otomatik bloke eder ve
    /// son kullanma tarihi yaklaşan kartları otomatik yeniler.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    RunEodBatchResponse RunEodBatch(RunEodBatchRequest request);

    /// <summary>
    /// Bir karta ait ekstre (hesap kesimi) geçmişini listeler.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetCardStatementsResponse GetCardStatements(GetCardStatementsRequest request);
}

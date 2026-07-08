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
    /// Belirtilen kartın limitini günceller. Limit düşüşü anında uygulanır; limit artışı ise
    /// maker-checker (çift onay) akışına girer ve yalnızca onay bekleyen bir talep oluşturur.
    /// </summary>
    /// <param name="request">Kart ID, yeni limit ve zorunlu gerekçe bilgileri</param>
    /// <returns>Güncellenmiş kart bilgisi (artışlarda onay bekleyen talep bilgisiyle birlikte)</returns>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    UpdateCardLimitResponse UpdateCardLimit(UpdateCardLimitRequest request);

    /// <summary>
    /// Onay bekleyen bir limit artış talebini karara bağlar (onay/red). Kararı veren kullanıcı,
    /// talebi giren kullanıcı ile aynı olamaz (four-eyes principle). Onayda yeni limit karta
    /// atomik olarak uygulanır ve PayCore tarafına senkronize edilir.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    DecideCardLimitChangeResponse DecideCardLimitChange(DecideCardLimitChangeRequest request);

    /// <summary>
    /// Limit artış taleplerini listeler (varsayılan olarak yalnızca onay bekleyenler).
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetLimitChangeRequestsResponse GetLimitChangeRequests(GetLimitChangeRequestsRequest request);

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

    /// <summary>
    /// PayCore ile senkronu belirsiz kalmış (ağ hatası nedeniyle ulaşılamamış veya süreç PayCore'u
    /// aramadan önce çökmüş) işlemleri tarayıp yeniden dener — bkz. boa_paycore_outbox.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    RetryPendingPaycoreSyncsResponse RetryPendingPaycoreSyncs(RetryPendingPaycoreSyncsRequest request);

    /// <summary>
    /// Kredi kartı başvurusu girer; karar motoru (skor + BDDK tavanı) senkron çalışır.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    ApplyForCreditCardResponse ApplyForCreditCard(ApplyForCreditCardRequest request);

    /// <summary>
    /// Kredi kartı başvurularını listeler.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetCardApplicationsResponse GetCardApplications(GetCardApplicationsRequest request);

    /// <summary>
    /// Manuel değerlendirme kuyruğundaki bir başvuruyu onaylar veya reddeder (four-eyes).
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    DecideCardApplicationResponse DecideCardApplication(DecideCardApplicationRequest request);

    /// <summary>
    /// Basılmış kartı TCKN doğrulaması ve müşteri PIN'i ile aktive eder.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    ActivateCardResponse ActivateCard(ActivateCardRequest request);

    /// <summary>
    /// Basılmış (InTransit) kartın müşteriye teslim edildiğini kaydeder; kart PendingActivation'a geçer.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    DeliverCardResponse DeliverCard(DeliverCardRequest request);

    /// <summary>
    /// BDDK/TCMB regülatuar raporlaması için günlük/portföy özeti döner.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetRegulatoryReportResponse GetRegulatoryReport(GetRegulatoryReportRequest request);

    /// <summary>
    /// Kayıp/çalıntı kart bildirimi: kart anında bloke edilir, aktif provizyonlar void edilir,
    /// PayCore bilgilendirilir ve opsiyonel olarak yedek kart talebi oluşturulur.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    ReportLostStolenResponse ReportLostStolenCard(ReportLostStolenRequest request);

    /// <summary>
    /// Kartın kalıcı olarak iptal edilmesi: aktif provizyonlar void edilir, kart Cancelled
    /// durumuna geçirilir ve PayCore bilgilendirilir.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CancelCardResponse CancelCard(CancelCardRequest request);

    /// <summary>
    /// Kart yenileme: aynı PAN ile yeni expiry/CVV/EMV üretilir, yeni kart PendingActivation
    /// durumunda oluşturulur; eski kart yeni kart aktive edilene kadar aktif kalır.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    RenewCardResponse RenewCard(RenewCardRequest request);

    /// <summary>
    /// Kart yeniden basım: fiziksel hasar, isim değişikliği, ürün upgrade/downgrade gibi
    /// nedenlerle yeni kart oluşturulur; eski kart anında iptal edilir.
    /// </summary>
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    ReissueCardResponse ReissueCard(ReissueCardRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CreateTemporaryLimitIncreaseResponse CreateTemporaryLimitIncrease(CreateTemporaryLimitIncreaseRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    RevertTemporaryLimitResponse RevertTemporaryLimit(RevertTemporaryLimitRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    UpdateSpendingLimitResponse UpdateSpendingLimit(UpdateSpendingLimitRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetSpendingLimitsResponse GetSpendingLimits(GetSpendingLimitsRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CreateInstallmentTransactionResponse CreateInstallmentTransaction(CreateInstallmentTransactionRequest request);
}

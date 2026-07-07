namespace BOA.Services.Card.Paycore;

/// <summary>
/// Bankanın kendi tarafında ("outbox") tuttuğu, PayCore'a bildirilmesi gereken bir işlemin senkronizasyon
/// durumu. Bu tablo, ledger kaydıyla AYNI yerel veritabanı transaction'ında (atomik) yazılır — süreç
/// PayCore'u aramadan ÖNCE çökse bile, "bunu PayCore'a bildirmem gerekiyordu" bilgisi diskte kalır ve
/// daha sonra bir mutabakat/yeniden deneme işlemiyle (bkz. CardService.RetryPendingPaycoreSyncs) tamamlanabilir.
/// Bu, dağıtık sistemlerdeki klasik "dual write" (iki farklı sisteme ayrı ayrı, atomik olmayan yazma)
/// probleminin standart çözümüdür (outbox pattern).
/// </summary>
public enum PaycoreOutboxStatus
{
    /// <summary>Banka tarafında yazıldı, PayCore'a henüz hiç bildirilmedi veya yanıt beklenmedi.</summary>
    Pending = 1,

    /// <summary>PayCore onayladı — iki taraf senkron.</summary>
    Confirmed = 2,

    /// <summary>PayCore reddetti — bankanın kendi kaydı ters kayıtla (reversal) telafi edildi.</summary>
    Declined = 3,

    /// <summary>
    /// PayCore'a ulaşılamadı (ağ hatası/timeout) — PayCore'un GERÇEK kararı bilinmiyor. Bilinçli olarak
    /// otomatik ters kayıt YAZILMAZ (belki PayCore aslında onayladı, yanıt bize ulaşmadı); bunun yerine
    /// yeniden deneme/mutabakat kuyruğunda kalır.
    /// </summary>
    FailedNeedsRetry = 4
}

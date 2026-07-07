# Active Context — BOA Card Management

## Current Focus
Kart Yaşam Döngüsü (Kayıp/Çalıntı, İptal, Yenileme, Yeniden Basım) uygulaması devam ediyor.

Tamamlanan:
- Contracts: BlockReason, CancellationReason, ReissueReason enum'ları; CardBlockHistoryDto; CardDto genişletme; 4 yeni Request/Response; ICardService 4 yeni operasyon; SetCardStatusRequest genişletme; RunEodBatchResponse.ReplacementCardsIssued
- Veri Katmanı: boa_cards 6 yeni kolon (block_reason, blocked_date, cancelled_date, cancellation_reason, previous_card_id, replaced_by_card_id); boa_card_block_history tablosu; 5 yeni SP (report_lost_stolen, void_active_auths, cancel, create_replacement, set_replacement_link); ExecuteSetStatus bloke/iptal tarih+nede kaydı yapacak şekilde genişletildi; ExecuteInsertCard previous_card_id desteği
- CardMappers: ToCardDto 6 yeni alan + ToCardBlockHistoryDto mapper

Kalan:
- FakePaycoreGateway: SetCardStatusCallCount + RenewCardCallCount tracker
- CardService: CreateReplacementCard (private helper) + 4 yeni public metot (ReportLostStolenCard, CancelCard, RenewCard, ReissueCard) + SetCardStatus PayCore senkronu + ActivateCard otomatik eski kart iptali + RunEodBatch gerçek kart yenileme
- REST + WCF + Controller: 4 yeni endpoint
- Web UI: modal'lar + kart kartı butonları
- Testler: CardLifecycleTests.cs (~30 test)
- Full build + test verification

## Recent Changes (2026-07-07)
1. **Kart Yaşam Döngüsü Adımlar 1-3**: Enum'lar, DTO'lar, Request/Response sınıfları, ICardService genişletme, veri katmanı (SqliteMockProvider yeni kolonlar/tablo/SP'ler), CardMappers güncellemeleri — hepsi build başarılı
2. **UpdateCardLimit**: Maker-checker (4-eyes) eklendi
3. **ComputeCreditScore**: Deterministik KKB formülü
4. **CreateTransaction + AuthorizeTransaction**: PayCore entegrasyonu

## Next Steps (Priority)
1. FakePaycoreGateway tracker ekle (SetCardStatusCallCount, RenewCardCallCount)
2. CardService.cs: CreateReplacementCard helper + 4 yeni operasyon + SetCardStatus/ActivateCard/EOD değişiklikleri
3. REST + WCF + Controller
4. Web UI
5. Testler
6. Full build + test

## Active Decisions
- Memory Bank kullanılıyor
- Kart yenileme: eski kart aktif kalır, yeni kart aktivasyonunda otomatik iptal
- Kart yeniden basım: eski kart anında iptal edilir
- PayCore hataları kart operasyonlarını engellemez (try/catch + audit log)
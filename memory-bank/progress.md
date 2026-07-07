# Progress — BOA Card Management

## What Works ✅
- Kart oluşturma (CreateCard): Luhn validasyonlu, BIN tabanlı marka/segment, CVV/CVV2 hash, EMV verileri
- Kart listeleme, limit güncelleme, durum değiştirme
- İşlem yönetimi (CreateTransaction): HSM PIN doğrulamalı, çift kayıt muhasebe
- Provizyon akışı (Authorize→Capture→Void)
- Şifre doğrulama (VerifyPin): HSM PIN Block
- Kredi kartı başvurusu (ApplyForCreditCard): Skorlama, BDDK tavanı, fraud kontrolü
- Başvuru değerlendirme (DecideCardApplication): 4-eyes prensibi
- Kart aktivasyonu (ActivateCard): TCKN + PIN ile
- Kart teslimatı (DeliverCard): InTransit → PendingActivation
- Gün sonu batch (RunEodBatch): Ekstre, faiz, blokaj, yenileme, başvuru basım
- BDDK/TCMB regülatuar raporlama
- PayCore outbox retry mekanizması
- Web UI (ASP.NET Core MVC), WPF client, Console client
- 83/83 test passing ✅

## In Progress 🔄
- **Kart Yaşam Döngüsü**: Kayıp/Çalıntı, İptal, Yenileme, Yeniden Basım
  - ✅ Adım 1: Contracts (Enum'lar, DTO'lar, Request/Response, ICardService)
  - ✅ Adım 2: Veri Katmanı (SqliteMockProvider yeni kolonlar/tablo/SP'ler)
  - ✅ Adım 3: CardMappers (ToCardDto + ToCardBlockHistoryDto)
  - ⬜ FakePaycoreGateway tracker
  - ⬜ CardService (CreateReplacementCard + 4 yeni operasyon + mevcut metot değişiklikleri)
  - ⬜ REST + WCF + Controller
  - ⬜ Web UI
  - ⬜ Testler (CardLifecycleTests.cs, ~30 test)
  - ⬜ Full build + test verification

## Known Issues ⚠️
- `CardService.cs` ~1800 satır — God Class, ileride split edilmeli
- PostgreSQL/Oracle migration script'leri güncellenmeli (yeni kolonlar için)

## Test Coverage (83/83) ✅
- Tüm testler geçiyor (CardApplicationFlow, CardCreation, AuthorizationFlow, Rbac, MakerChecker, Transaction)
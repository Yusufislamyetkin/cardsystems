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
- 60/83 test passing

## Known Issues ⚠️
- 23 test failing: PayCore gateway entegrasyon testleri, maker-checker senaryoları (önceden mevcut)
- `CardService.cs` ~1700 satır — God Class, ileride split edilmeli
- PostgreSQL/Oracle migration script'leri güncellenmeli (yeni kolonlar için)
- CardDto testlerde bazı alanlar nullable olarak geliyor (testler düzeltilmeli)

## Test Coverage (60/83)
- CardApplicationFlowTests: 17/23 passing
- CardCreationTests: 8/11 passing
- AuthorizationFlowTests: 3/8 passing
- RbacTests: 4/5 passing
- MakerCheckerLimitTests: 6/10 passing
- TransactionTests: 4/6 passing
- Diğerleri: 18/20 passing
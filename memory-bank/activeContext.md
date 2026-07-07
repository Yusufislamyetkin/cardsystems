# Active Context — BOA Card Management

## Current Focus
Bankacılık seviyesinde kart oluşturma işlemlerini tamamlamak (Luhn, CVV, EMV, CardBrand/Product).

## Recent Changes (2026-07-07)
1. **CardBrand/CardProduct enums** — Visa, Mastercard, Troy, Amex; Classic, Gold, Platinum, Business, Premium
2. **LuhnHelper** — ISO 7812-1 Luhn algoritması, check digit hesaplama
3. **CvvHelper** — CVV/CVV2/iCVV üretimi (HMAC-SHA256 tabanlı, 3DES simülasyonu)
4. **EmvHelper** — Track2, Service Code, EMBOSS formatlama, BDDK limit tavanı, kart vade süreleri
5. **CardDto** — EmbossName, CardBrand, CardProduct, Cvv2Hash, CvvHash, ServiceCode, Track2Data
6. **CardService.CreateCard** — Luhn + BIN + EMV + CVV entegrasyonu
7. **SqliteMockProvider** — BIN/kart tablolarına yeni sütunlar, 7 BIN girişi
8. **Missing interface impls** — ApplyForCreditCard, ActivateCard, DeliverCard, DecideCardApplication, GetRegulatoryReport, vb.
9. **SetCardStatus** — Cancelled/PendingActivation state guards
10. **EOD Batch** — CardsIssued + ApplicationsExpired eklemesi
11. **Memory Bank** — 6 core dosya oluşturuldu + .clinerules güncellendi

## Next Steps (Priority)
1. Çalışmaya nereden devam edileceğine `.clinerules` yönlendirmesine göre karar ver
2. Test kapsamını genişlet (yeni CVV/EMV/Luhn helper'ları için)
3. PayCore entegrasyon testlerini düzelt (23 failing test)
4. Kart teslimat/aktivasyon akışını tamamla (InTransit → DeliverCard → PendingActivation → ActivateCard)
5. PostgreSQL/Oracle migration script'lerini yeni kolonlarla güncelle

## Active Decisions
- Memory Bank kullanılıyor → kod okuma azaltması için
- Token tasarrufu: `.clinerules` talimatları ile gereksiz dosya okumaları önlendi
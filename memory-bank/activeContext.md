# Active Context — BOA Card Management

## Current Focus
**Limit Yönetimi: Kurumsal Bankacılık Seviyesinde 6 Özellik** (11 adım).

### Tamamlanan (2/11)
1. ✅ Adım 1 — Bug Fix + Contracts: Bug fix (p_only_pending), 3 Enum, 3 DTO, CardDto genişletme, 4 Request/Response, ICardService 5 operasyon, CardMappers genişletme, stub metotlar
2. ✅ Adım 2 — Veri Katmanı: 5 yeni TryAddColumn, 4 yeni tablo (temp_limits, spending_limits, installment_plans, mcc_rules), 2 SP

### Kalan (9/11)
3. Adım 3: CardMappers — ToCardDto yeni kolonlar + ToCardSubLimitsDto
4. Adım 4: CardService — İlk Limit Tahsisi + BDDK (CreateCardCore)
5. Adım 5: CardService — Geliştirilmiş Limit Artış/Azaltma
6. Adım 6: CardService — Geçici Limit Artışı
7. Adım 7: CardService — Harcama Limitleri
8. Adım 8: CardService — Taksit/Nakit Avans
9. Adım 9: REST + WCF Endpoints
10. Adım 10: Web UI
11. Adım 11: Testler (~32 test)

## Git (Latest: `e272099`)
- `834fae0` — Adim 1 (Bug fix + Contracts)
- `e272099` — Adim 2 (Veri Katmani)

## Test Status
- 103 total: 101 passing, 2 pre-existing PayCore failures
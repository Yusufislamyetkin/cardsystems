# BOA Card Management System — Project Brief

## Core Purpose
Bankacılık seviyesinde (production-grade) kredi kartı ve banka kartı (debit) oluşturma, yönetme ve izleme sistemi. Gerçek banka operasyonlarına yakın bir kart yönetim platformu.

## Key Requirements
1. **Kart Oluşturma**: Luhn geçerli kart numarası, BIN tabanlı marka/segment belirleme, CVV/CVV2 üretimi, EMV Track2 verisi, EMBOSS isim formatlama
2. **Kart Yaşam Döngüsü**: Active → Blocked → Cancelled, PendingActivation → Active (TCKN + PIN ile), InTransit → Deliver → PendingActivation
3. **Kredi Kartı Başvuru Akışı**: Apply → Skorlama → AutoApprove / ManualReview / AutoReject → Decide (4-eyes) → EOD Issuance
4. **İşlem Yönetimi**: Harcama/Çekim (HSM PIN doğrulamalı), Bakiye/Limit kontrolü, Çift Kayıt Muhasebe (Ledger)
5. **Provizyon Akışı**: Authorize → Capture/Void, ISO 8583 DE39 yanıt kodları
6. **Gün Sonu (EOD) Batch**: Ekstre kesimi, gecikme faizi, otomatik blokaj, kart yenileme, başvuru basım
7. **Güvenlik**: PCI-DSS maskeleme, AES-256 şifreleme, HSM PIN Block (ISO 9564), RBAC yetkilendirme
8. **Regülasyon**: BDDK limit tavanı, 4-göz prensibi (maker-checker), BDDK/TCMB raporlaması

## Tech Stack
- .NET 9.0, C#, CoreWCF (SOAP), ASP.NET Core (REST)
- SQLite (mock/test), Oracle/PostgreSQL (production scripts)
- xUnit test framework
- PayCore entegrasyonu (mock gateway)
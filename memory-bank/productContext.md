# Product Context — BOA Card Management

## Why This Project Exists
Bankaların kart operasyonlarını simüle eden, gerçek bankacılık seviyesinde bir kart yönetim sistemi. Eğitim/demo amaçlı olarak banka kartı ve kredi kartı oluşturma, yönetme, işlem ve raporlama süreçlerini kapsar.

## Problems It Solves
- Kart numarası üretiminde bankacılık standartları (Luhn, BIN, EMV)
- PCI-DSS uyumlu güvenli kart verisi saklama
- Kredi kartı başvuru ve onay süreçleri (skorlama, BDDK limit tavanı)
- Çift kayıtlı muhasebe ile işlem takibi
- Gün sonu batch süreçleri (ekstre, faiz, blokaj, yenileme)

## User Experience Goals
- SOAP (WCF) ve REST (JSON) çift arayüz desteği
- Role-based access (BranchTeller, CardOperationsAdmin)
- Gerçek zamanlı WCF execution log tracing
- Web UI (ASP.NET Core MVC) ile görsel yönetim
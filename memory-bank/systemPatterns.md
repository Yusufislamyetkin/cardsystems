# System Patterns — BOA Card Management

## Architecture
```
src/
├── BOA.Common.Contracts/     # DTOs, Enums, Request/Response, ServiceContracts (DataContract)
├── BOA.Services.Card/        # Business logic: CardService (ICardService impl), HSM, PayCore, Mappers
│   ├── CardService.cs        # ~1700 lines - all service operations
│   ├── Hsm/HsmEngine.cs      # ISO 9564 PIN Block generation & verification
│   └── Paycore/              # External card processing gateway (mock)
├── BOA.Data/                 # DB layer: DbManager, SqliteMockProvider, Helpers (Luhn, Cvv, Emv)
├── BOA.App/                  # ASP.NET Core host: Controllers (REST), Program.cs, wwwroot (Web UI)
├── BOA.Client.Wpf/           # WPF client via CoreWCF channel
├── BOA.Client.Console/       # Console test client
└── BOA.Tests/                # xUnit: CardApplicationFlow, CardCreation, Authorization, MakerChecker, etc.
```

## Key Design Patterns
- **WCF ServiceContract**: ICardService defines all operations
- **Stored Procedure Pattern**: SqliteMockProvider simulates DB procedures (sp_boa_card_create, etc.)
- **Outbox Pattern**: boa_paycore_outbox for atomic PayCore sync
- **Double-Entry Ledger**: Her işlem debit/credit pair olarak yevmiye defterine yazılır
- **Maker-Checker (4-eyes)**: Limit artışı/başvuru onayı iki farklı kullanıcı gerektirir
- **Mapper Pattern**: CardMappers.cs - DataRow → DTO dönüşümleri

## Component Relationships
- CardService → DbManager → SqliteMockProvider (SQLite)
- CardService → HsmEngine (PIN doğrulama)
- CardService → IPaycoreGateway (harici kart işlemleri)
- CardApiController → CardService (REST wrapper)
- WPF Client → CardService (CoreWCF SOAP channel)

## Critical Paths
1. **CreateCard**: Validation → BIN lookup → Luhn generation → AES encrypt → HSM PIN block → EMV data → DB insert
2. **Authorize→Capture**: PIN verify → Limit check → Hold → Ledger entry (capture'da)
3. **EOD Batch**: Statement (ekstre) → Interest (faiz) → AutoBlock → CardRenew → ApplicationIssuance
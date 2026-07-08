# Technical Context — BOA Card Management

## Technologies
| Layer | Technology | Version |
|-------|-----------|---------|
| Runtime | .NET | 9.0 |
| Language | C# | 12+ |
| SOAP | CoreWCF | net9.0 |
| REST | ASP.NET Core | net9.0 |
| DB (Mock) | SQLite (Microsoft.Data.Sqlite) | - |
| DB (Prod) | Oracle + PostgreSQL scripts | - |
| Tests | xUnit | - |
| Client | WPF (CoreWCF), Console | - |

## Development Setup
```bash
cd c:/Users/Yusuf/Desktop/BOA
dotnet build BOA.sln
dotnet test src/BOA.Tests/BOA.Tests.csproj
```

## Key Files by Size
- `CardService.cs` — ~3000 lines (monolithic business logic + kart yaşam döngüsü + limit yönetimi stub'ları)
- `SqliteMockProvider.cs` — ~2500 lines (SQLite stored procedure simulation + yeni limit tabloları)
- `CardLifecycleTests.cs` — ~420 lines (20 test)
- `CardApplicationFlowTests.cs` — ~525 lines

## Technical Constraints
- No external DB required for development (SQLite mock)
- PCI-DSS: PAN encrypted (AES-256), masked in responses
- HSM: PIN Block uses XOR simulation (real HSM would use 3DES)
- RBAC: Mock JWT tokens (MOCK_JWT_ADMIN_TOKEN, MOCK_JWT_TELLER_TOKEN)

## Test Status (as of 2026-07-08)
- 103 total tests
- 101 passing ✅
- 2 pre-existing failures (AuthorizationFlowTests — PayCore simülasyonu)

## Recent Schema Changes
- `boa_cards`: +5 limit yönetimi kolonu (cash_advance_limit, installment_limit, daily_atm_limit, daily_pos_limit, monthly_spending_limit)
- 4 yeni tablo: `boa_temporary_limits`, `boa_spending_limits`, `boa_installment_plans`, `boa_mcc_installment_rules`
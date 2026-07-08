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
- `CardService.cs` — ~2700 lines (monolithic business logic + kart yaşam döngüsü)
- `SqliteMockProvider.cs` — ~2200 lines (SQLite stored procedure simulation)
- `CardApplicationFlowTests.cs` — ~525 lines
- `CardLifecycleTests.cs` — ~420 lines (20 yeni test)
- `CardServiceTestHarness.cs` — ~80 lines (test infra)

## Technical Constraints
- No external DB required for development (SQLite mock)
- PCI-DSS: PAN encrypted (AES-256), masked in responses
- HSM: PIN Block uses XOR simulation (real HSM would use 3DES)
- RBAC: Mock JWT tokens (MOCK_JWT_ADMIN_TOKEN, MOCK_JWT_TELLER_TOKEN)
- LF/CRLF line endings warning (Git for Windows)

## Test Status (as of 2026-07-08)
- 103 total tests
- 101 passing ✅
- 2 pre-existing failures (AuthorizationFlowTests — PayCore simülasyonu, kart değişiklikleriyle ilgili değil)
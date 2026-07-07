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
- `CardService.cs` — ~1700 lines (monolithic business logic)
- `SqliteMockProvider.cs` — ~1900 lines (SQLite stored procedure simulation)
- `CardApplicationFlowTests.cs` — ~525 lines
- `CardServiceTestHarness.cs` — ~80 lines (test infra)

## Technical Constraints
- No external DB required for development (SQLite mock)
- PCI-DSS: PAN encrypted (AES-256), masked in responses
- HSM: PIN Block uses XOR simulation (real HSM would use 3DES)
- RBAC: Mock JWT tokens (MOCK_JWT_ADMIN_TOKEN, MOCK_JWT_TELLER_TOKEN)
- LF/CRLF line endings warning (Git for Windows)

## Test Status (as of 2026-07-07)
- 83 total tests
- 83 passing ✅
- 0 failing
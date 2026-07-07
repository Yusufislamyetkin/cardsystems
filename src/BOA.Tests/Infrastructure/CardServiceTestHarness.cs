using BOA.Data;
using BOA.Data.Providers;
using BOA.Services.Card;
using Microsoft.AspNetCore.Http;

namespace BOA.Tests.Infrastructure;

public enum TestRole
{
    Teller,
    Admin
}

/// <summary>
/// Her testin, birbirinden tamamen izole (geçici dosya tabanlı) bir SQLite veritabanına karşı
/// çalışan, tam olarak bağımlılıkları enjekte edilmiş bir CardService örneği kurmasını sağlar.
/// Gerçek DB'ye karşı (Postgres/Oracle) manuel doğrulamalar bu oturumda ayrıca yapıldı; bu testler
/// SqliteMockProvider üzerinden hızlı, deterministik regresyon koruması sağlar.
/// </summary>
public sealed class CardServiceTestHarness : IDisposable
{
    public CardService Service { get; }

    /// <summary>
    /// Testlerin, bankanın kendi limit kontrolü onaylasa bile dış sistemin (PayCore) bir sonraki
    /// provizyonu reddetmesini simüle edebilmesi için doğrudan eriştiği sahte gateway.
    /// </summary>
    public FakePaycoreGateway PaycoreGateway { get; } = new();

    /// <summary>
    /// Testlerin (örn. EOD batch senaryolarında bir ekstrenin vade tarihini geçmişe almak için)
    /// veritabanına doğrudan erişebilmesi için bağlantı dizesi.
    /// </summary>
    public string ConnectionString => $"Data Source={_dbPath}";

    private readonly string _dbPath;
    private readonly DefaultHttpContext _httpContext;

    public CardServiceTestHarness()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"boa_test_{Guid.NewGuid():N}.db");
        var provider = new SqliteMockProvider($"Data Source={_dbPath}");
        var dbManager = new DbManager(provider);

        var accessor = new HttpContextAccessor();
        _httpContext = new DefaultHttpContext();
        accessor.HttpContext = _httpContext;

        Service = new CardService(dbManager, accessor, new CurrentUserContext(), PaycoreGateway);
        SetRole(TestRole.Teller);
    }

    /// <summary>
    /// Sonraki servis çağrılarında kullanılacak simüle edilmiş rolü (SecurityToken) değiştirir.
    /// </summary>
    public void SetRole(TestRole role)
    {
        _httpContext.Request.Headers["X-Security-Token"] = role == TestRole.Admin ? "MOCK_JWT_ADMIN_TOKEN" : "MOCK_JWT_TELLER_TOKEN";
    }

    /// <summary>
    /// SecurityToken header'ını kaldırır — token göndermeyen bir istemciyi simüle eder.
    /// </summary>
    public void ClearRole()
    {
        _httpContext.Request.Headers.Remove("X-Security-Token");
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
        catch
        {
            // Test sonunda geçici dosyanın silinememesi (örn. dosya kilidi) testin başarısını etkilemez.
        }
    }
}

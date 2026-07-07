using System.Data;
using System.Diagnostics;
using BOA.Data.Providers;

namespace BOA.Data;

/// <summary>
/// BOA mimarisinde veritabanı işlemlerini yöneten merkezi yöneticidir (Database Manager).
/// Servis katmanları veritabanına erişmek için doğrudan bağlantı açmaz; bu yöneticinin
/// sunduğu ExecuteReader/ExecuteNonQuery metotlarını çağırır.
/// </summary>
public class DbManager
{
    private readonly IBoaDbProvider _dbProvider;

    /// <summary>
    /// Gerçek zamanlı izleme (Tracer) için veritabanı işlem günlüklerini hafızada tutan listedir.
    /// Arayüz (UI) katmanına SQL loglarını basmak için kullanılır.
    /// </summary>
    public static readonly List<string> SqlExecutionLogs = new List<string>();

    /// <summary>
    /// Bağımlılık Enjeksiyonu (DI) ile veritabanı sağlayıcısını alır.
    /// Eğer boş geçilirse varsayılan olarak SqliteMockProvider kullanılır.
    /// </summary>
    public DbManager(IBoaDbProvider? dbProvider = null)
    {
        // Sağlayıcı tanımlanmamışsa otomatik olarak SQLite Mock sağlayıcısı devreye alınır.
        _dbProvider = dbProvider ?? new SqliteMockProvider();
    }

    /// <summary>
    /// Stored Procedure adını ve parametrelerini alarak ilgili sağlayıcıda çalıştırır.
    /// Sonuçları DataTable olarak döner ve çalıştırılan komutun detaylarını loglar.
    /// </summary>
    public DataTable ExecuteReader(string spName, Dictionary<string, object> parameters)
    {
        // >>> ADIM 10: DbManager sadece bir ara katman — zamanlama/loglama dışında iş kuralı YOK.
        // Asıl mantık _dbProvider.ExecuteStoredProcedureReader içinde (aktif sağlayıcıya göre
        // SqliteMockProvider/PostgresProvider/OracleProvider'dan biri).
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Sağlayıcıyı tetikle
            var result = _dbProvider.ExecuteStoredProcedureReader(spName, parameters);
            stopwatch.Stop();

            // Log kaydını oluştur ve tracer listesine ekle
            LogExecution(spName, parameters, stopwatch.ElapsedMilliseconds, result.Rows.Count);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogException(spName, parameters, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
    }

    /// <summary>
    /// Veri seti döndürmeyen Stored Procedure'leri tetikler ve etkilenen satır sayısını döner.
    /// </summary>
    public int ExecuteNonQuery(string spName, Dictionary<string, object> parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            int affectedRows = _dbProvider.ExecuteStoredProcedureNonQuery(spName, parameters);
            stopwatch.Stop();

            LogExecution(spName, parameters, stopwatch.ElapsedMilliseconds, affectedRows);

            return affectedRows;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogException(spName, parameters, stopwatch.ElapsedMilliseconds, ex);
            throw;
        }
    }

    /// <summary>
    /// Başarılı veritabanı işlemlerini formatlayarak tracer loglarına yazar.
    /// </summary>
    private void LogExecution(string spName, Dictionary<string, object> parameters, long elapsedMs, int affectedRows)
    {
        // Parametreleri "param1=deger1, param2=deger2" formatına dönüştürüyoruz
        var paramListStr = string.Join(", ", parameters.Select(p => $"{p.Key} = '{MaskIfSensitive(p.Key, p.Value)}'"));

        string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] [DB EXEC] EXEC {spName} {paramListStr} | Dönen/Etkilenen Satır: {affectedRows} | Süre: {elapsedMs} ms";
        
        lock (SqlExecutionLogs)
        {
            SqlExecutionLogs.Add(logMessage);
            // Listenin şişmesini engellemek için son 100 logu tutuyoruz
            if (SqlExecutionLogs.Count > 100)
            {
                SqlExecutionLogs.RemoveAt(0);
            }
        }
    }

    /// <summary>
    /// Hata alan veritabanı işlemlerini tracer loglarına yazar.
    /// </summary>
    private void LogException(string spName, Dictionary<string, object> parameters, long elapsedMs, Exception ex)
    {
        var paramListStr = string.Join(", ", parameters.Select(p => $"{p.Key} = '{MaskIfSensitive(p.Key, p.Value)}'"));
        string logMessage = $"[{DateTime.Now:HH:mm:ss.fff}] [DB ERROR] EXEC {spName} {paramListStr} | HATA: {ex.Message} | Süre: {elapsedMs} ms";

        lock (SqlExecutionLogs)
        {
            SqlExecutionLogs.Add(logMessage);
            if (SqlExecutionLogs.Count > 100)
            {
                SqlExecutionLogs.RemoveAt(0);
            }
        }
    }

    // PCI-DSS: PAN/PIN/CVV gibi hassas alanlar, şifreli/hash'lenmiş halde bile olsa
    // tracer loglarına (ve dolayısıyla /api/tracer/sql-logs uç noktasına) açık yazılmaz.
    private static readonly string[] SensitiveParamMarkers = { "pan", "pin", "cvv", "cvc", "password", "pwd" };

    private static object MaskIfSensitive(string paramName, object value)
    {
        var nameLower = paramName.ToLowerInvariant();
        if (SensitiveParamMarkers.Any(marker => nameLower.Contains(marker)))
        {
            return "***MASKED***";
        }

        return value;
    }
}

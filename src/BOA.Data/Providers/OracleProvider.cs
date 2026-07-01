using System.Data;
using Oracle.ManagedDataAccess.Client;

namespace BOA.Data.Providers;

/// <summary>
/// Oracle veritabanı sağlayıcı sınıfıdır.
/// Oracle PL/SQL Paketleri (Packages) altında yer alan Stored Procedure'leri tetiklemek için
/// Oracle.ManagedDataAccess.Core (ODP.NET Managed Driver) kütüphanesini kullanır.
/// </summary>
public class OracleProvider : IBoaDbProvider
{
    private readonly string _connectionString;

    /// <summary>
    /// Oracle bağlantı dizesi ile sağlayıcıyı ilklendirir.
    /// Şifreli bağlantı dizesi (ENCRYPTED:) formatındaysa çalışma zamanında şifresini çözer.
    /// </summary>
    public OracleProvider(string connectionString)
    {
        _connectionString = DecryptConnectionString(connectionString);
    }

    /// <summary>
    /// Oracle Stored Procedure'lerini tetikler. Oracle'da select sorgularının geri dönebilmesi için
    /// prosedür parametrelerinde bir 'SYS_REFCURSOR' (Output Parameter) bulunması zorunludur.
    /// </summary>
    public DataTable ExecuteStoredProcedureReader(string spName, Dictionary<string, object> parameters)
    {
        var dt = new DataTable();

        using (var connection = new OracleConnection(_connectionString))
        {
            // C# servis katmanından PostgreSQL formatında gelen prosedür adını,
            // Oracle kurumsal paket (PKG_BOA_CARD.SP_...) yapısına dönüştürüyoruz.
            string oracleSpName = MapToOracleSp(spName);

            using (var command = new OracleCommand(oracleSpName, connection))
            {
                command.CommandType = CommandType.StoredProcedure;

                // İstekten gelen parametreleri Oracle command nesnesine bağlıyoruz
                foreach (var param in parameters)
                {
                    // Oracle veritabanı boş parametreler için DBNull.Value kabul eder.
                    var value = param.Value ?? DBNull.Value;
                    command.Parameters.Add(param.Key, value);
                }

                command.BindByName = true;

                // ORACLE ÖZEL KURALI: Dönen veri setini yakalamak için SYS_REFCURSOR parametresini ekliyoruz.
                // Prototipimizdeki "SP_CREATE_TRANSACTION" prosedürü 'o_trans_cursor', diğerleri 'o_cursor' parametresini kullanır.
                string refCursorParamName = oracleSpName.Contains("SP_CREATE_TRANSACTION") ? "o_trans_cursor" : "o_cursor";
                
                var refCursorParam = new OracleParameter(refCursorParamName, OracleDbType.RefCursor)
                {
                    Direction = ParameterDirection.Output
                };
                command.Parameters.Add(refCursorParam);

                connection.Open();

                // Verileri DataTable'a dolduruyoruz
                using (var adapter = new OracleDataAdapter(command))
                {
                    adapter.Fill(dt);
                }
            }
        }

        // Hata ayıklama için kolonları logluyoruz
        var originalCols = string.Join(", ", dt.Columns.Cast<DataColumn>().Select(c => c.ColumnName));
        DbManager.SqlExecutionLogs.Add($"[DB DIAG] Oracle'dan Dönen Orijinal Kolonlar: {originalCols}");

        // Oracle veritabanından dönen kolon isimleri büyük harf (Örn: CARD_ID) olduğu için,
        // C# mapping kodumuzun (card_id) çalışabilmesi amacıyla kolon isimlerini küçük harfe çeviriyoruz.
        foreach (DataColumn col in dt.Columns)
        {
            col.ColumnName = col.ColumnName.ToLowerInvariant();
        }

        return dt;
    }

    /// <summary>
    /// Oracle'da durum güncelleme gibi veri dönmeyen işlemleri de yine Reader üzerinden 
    /// (WCF model güncellemesi için güncel satırı dönen prosedür tasarımı gereği) işletiyoruz.
    /// </summary>
    public int ExecuteStoredProcedureNonQuery(string spName, Dictionary<string, object> parameters)
    {
        ExecuteStoredProcedureReader(spName, parameters);
        return 1;
    }

    /// <summary>
    /// PostgreSQL prosedür isimlerini Oracle Paket/Prosedür karşılıklarına eşler.
    /// Bu sayede C# Servis katmanında kod değişikliği yapmadan veri tabanı bağımsızlığı korunur.
    /// </summary>
    private string MapToOracleSp(string pgSpName)
    {
        string cleanName = pgSpName.ToLowerInvariant().Trim();
        return cleanName switch
        {
            "sp_boa_card_create" => "PKG_BOA_CARD.SP_CREATE_CARD",
            "sp_boa_card_get_list" => "PKG_BOA_CARD.SP_GET_CARD_LIST",
            "sp_boa_card_update_limit" => "PKG_BOA_CARD.SP_UPDATE_LIMIT",
            "sp_boa_card_set_status" => "PKG_BOA_CARD.SP_SET_CARD_STATUS",
            "sp_boa_card_create_transaction" => "PKG_BOA_CARD.SP_CREATE_TRANSACTION",
            "sp_boa_card_get_transactions" => "PKG_BOA_CARD.SP_GET_TRANSACTIONS",
            "sp_boa_card_get_secure_details" => "PKG_BOA_CARD.SP_GET_SECURE_DETAILS",
            "sp_boa_bin_lookup" => "PKG_BOA_CARD.SP_BIN_LOOKUP",
            "sp_boa_auth_create" => "PKG_BOA_CARD.SP_AUTH_CREATE",
            "sp_boa_auth_capture" => "PKG_BOA_CARD.SP_AUTH_CAPTURE",
            "sp_boa_auth_void" => "PKG_BOA_CARD.SP_AUTH_VOID",
            "sp_boa_statement_create" => "PKG_BOA_CARD.SP_STATEMENT_CREATE",
            "sp_boa_statement_get_open" => "PKG_BOA_CARD.SP_STATEMENT_GET_OPEN",
            "sp_boa_statement_get_list" => "PKG_BOA_CARD.SP_STATEMENT_GET_LIST",
            "sp_boa_statement_mark_interest_applied" => "PKG_BOA_CARD.SP_STATEMENT_MARK_INTEREST_APPLIED",
            "sp_boa_card_renew" => "PKG_BOA_CARD.SP_CARD_RENEW",
            _ => pgSpName.ToUpperInvariant()
        };
    }

    /// <summary>
    /// Şifrelenmiş bağlantı dizesini çözer ve kurumsal bağlantı havuzu (pooling) parametrelerini ekler.
    /// </summary>
    private string DecryptConnectionString(string connStr)
    {
        string raw = connStr;
        if (connStr.StartsWith("ENCRYPTED:", StringComparison.OrdinalIgnoreCase))
        {
            string cipherText = connStr.Substring("ENCRYPTED:".Length);
            try
            {
                byte[] data = Convert.FromBase64String(cipherText);
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = (byte)(data[i] ^ 90); // XOR 0x5A
                }
                raw = System.Text.Encoding.UTF8.GetString(data);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Kurumsal veritabanı şifrelenmiş bağlantı dizesi çözülemedi!", ex);
            }
        }

        // Kurumsal Bağlantı Havuzu (Connection Pooling) ayarlarının eklenmesi
        if (!raw.Contains("Max Pool Size", StringComparison.OrdinalIgnoreCase))
        {
            if (!raw.EndsWith(";")) raw += ";";
            raw += "Min Pool Size=5;Max Pool Size=50;Connection Lifetime=60;Connection Timeout=15;Pooling=true;";
        }
        return raw;
    }
}

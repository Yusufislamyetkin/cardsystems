using System.Data;
using Npgsql;
using NpgsqlTypes;

namespace BOA.Data.Providers;

/// <summary>
/// PostgreSQL veritabanı sağlayıcı sınıfıdır.
/// PL/pgSQL ile yazılmış fonksiyonları (Stored Procedure) tetiklemek için ADO.NET Npgsql kütüphanesini kullanır.
/// </summary>
public class PostgresProvider : IBoaDbProvider
{
    private readonly string _connectionString;

    /// <summary>
    /// Sağlayıcıyı belirli bir bağlantı dizesi (Connection String) ile ilklendirir.
    /// </summary>
    public PostgresProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// PostgreSQL fonksiyonlarını (CREATE FUNCTION ... RETURNS TABLE) çağırır.
    /// Not: Bu projedeki tüm boa_* rutinleri FUNCTION olarak tanımlıdır, PROCEDURE değil.
    /// Npgsql'in CommandType.StoredProcedure modu her zaman "CALL fn(...)" üretir ve CALL
    /// yalnızca PROCEDURE'lerde çalışır — FUNCTION'a karşı çalıştırılınca "does not exist" hatası verir.
    /// Bu yüzden burada bilerek düz metin "SELECT * FROM fn(@p1, @p2, ...)" sorgusu kuruyoruz.
    /// </summary>
    public DataTable ExecuteStoredProcedureReader(string spName, Dictionary<string, object> parameters)
    {
        var dataTable = new DataTable();

        using (var connection = new NpgsqlConnection(_connectionString))
        using (var command = new NpgsqlCommand(BuildFunctionCallSql(spName, parameters), connection))
        {
            command.CommandType = CommandType.Text;

            foreach (var param in parameters)
            {
                var value = param.Value ?? DBNull.Value;
                command.Parameters.AddWithValue(param.Key, value);
            }

            connection.Open();

            using (var adapter = new NpgsqlDataAdapter(command))
            {
                adapter.Fill(dataTable);
            }
        }

        NormalizeOutputColumnNames(dataTable);
        return dataTable;
    }

    // PL/pgSQL'de RETURNS TABLE(...) ile bildirilen çıktı kolonları, fonksiyon gövdesi içinde
    // aynı isimli tablo kolonlarıyla çakışıp "ambiguous column reference" hatası vermesin diye
    // "o_" öneki ile tanımlanır (o_card_id, o_limit, ...). C# tarafı ise servis katmanının
    // Oracle/SQLite ile ortak kullandığı önek'siz isimleri (card_id, card_limit, ...) beklediği
    // için burada dönüştürülür.
    private static readonly Dictionary<string, string> OutputColumnRenames = new()
    {
        ["o_limit"] = "card_limit"
    };

    private static void NormalizeOutputColumnNames(DataTable dt)
    {
        foreach (DataColumn col in dt.Columns)
        {
            string name = col.ColumnName;
            if (OutputColumnRenames.TryGetValue(name, out var renamed))
            {
                col.ColumnName = renamed;
            }
            else if (name.StartsWith("o_", StringComparison.OrdinalIgnoreCase))
            {
                col.ColumnName = name.Substring(2);
            }
        }
    }

    /// <summary>
    /// Veri seti döndürmeyen çağrılar için de aynı FUNCTION çağrı biçimini kullanır
    /// (bu projedeki tüm rutinler sonuç seti döndürdüğünden ExecuteNonQuery yalnızca satır sayısı için kullanılır).
    /// </summary>
    public int ExecuteStoredProcedureNonQuery(string spName, Dictionary<string, object> parameters)
    {
        int affectedRows;

        using (var connection = new NpgsqlConnection(_connectionString))
        using (var command = new NpgsqlCommand(BuildFunctionCallSql(spName, parameters), connection))
        {
            command.CommandType = CommandType.Text;

            foreach (var param in parameters)
            {
                var value = param.Value ?? DBNull.Value;
                command.Parameters.AddWithValue(param.Key, value);
            }

            connection.Open();
            using var reader = command.ExecuteReader();
            affectedRows = 0;
            while (reader.Read()) affectedRows++;
        }

        return affectedRows;
    }

    private static string BuildFunctionCallSql(string spName, Dictionary<string, object> parameters)
    {
        string argList = string.Join(", ", parameters.Keys.Select(k => $"{k} := @{k}"));
        return $"SELECT * FROM {spName}({argList})";
    }
}

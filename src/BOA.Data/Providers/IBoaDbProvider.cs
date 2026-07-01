using System.Data;

namespace BOA.Data.Providers;

/// <summary>
/// BOA veri erişim katmanında veritabanı işlemlerini soyutlayan arayüzdür.
/// BOA mimarisinde veritabanı bağımsızlığı (Database Independence) sağlamak amacıyla
/// Oracle, PostgreSQL veya Mock (Test) ortamı sağlayıcıları bu arayüzü implemente eder.
/// </summary>
public interface IBoaDbProvider
{
    /// <summary>
    /// Bir Stored Procedure'ü tetikler ve dönen sonuç kümesini (ResultSet) bir DataTable olarak geri verir.
    /// Liste çekme, detay okuma gibi sorgu (SELECT) işlemlerinde kullanılır.
    /// </summary>
    /// <param name="spName">Tetiklenecek Stored Procedure veya Fonksiyon adı (Örn: "sp_boa_card_get_list")</param>
    /// <param name="parameters">Prosedüre gönderilecek anahtar-değer parametre listesi</param>
    /// <returns>Dönen satırları içeren DataTable</returns>
    DataTable ExecuteStoredProcedureReader(string spName, Dictionary<string, object> parameters);

    /// <summary>
    /// Bir Stored Procedure'ü tetikler ve etkilenen satır sayısını döner.
    /// Güncelleme, silme gibi doğrudan veri seti döndürmeyen komut işlemlerinde kullanılır.
    /// </summary>
    /// <param name="spName">Tetiklenecek Stored Procedure adı</param>
    /// <param name="parameters">Parametre listesi</param>
    /// <returns>Etkilenen satır sayısı</returns>
    int ExecuteStoredProcedureNonQuery(string spName, Dictionary<string, object> parameters);
}

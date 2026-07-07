using BOA.Common.Contracts.Enums;
using Microsoft.Data.Sqlite;

namespace BOA.Services.Card.Paycore;

/// <summary>
/// IPaycoreGateway'in simülasyon amaçlı uygulamasıdır. Gerçek bir PayCore entegrasyonunda bu sınıfın
/// yerini bir HTTP/SOAP istemcisi alırdı; burada "PayCore bizden tamamen ayrı bir sistemdir" gerçeğini
/// somutlaştırmak için KENDİ bağımsız SQLite veritabanını kullanır (bankanın boa_mock.db'sinden ayrı).
/// Bu iki veritabanı arasında hiçbir ortak transaction/foreign key yoktur — tam olarak gerçek hayattaki
/// gibi, aralarındaki tek bağ "referans numarası" (paycore_reference) alışverişidir.
/// </summary>
public class PaycoreMockGateway : IPaycoreGateway
{
    private readonly string _connectionString;

    public PaycoreMockGateway() : this("Data Source=paycore_mock.db")
    {
    }

    /// <summary>Testlerin izole bir dosya kullanabilmesi için eklenmiş aşırı yükleme.</summary>
    public PaycoreMockGateway(string connectionString)
    {
        _connectionString = connectionString;
        InitializeSchema();
    }

    /// <summary>
    /// Yalnızca testler/öğretim amaçlı: bankanın kendi limit kontrolü onaylasa bile, PayCore'un
    /// (örn. kendi risk motoru, switch'ten dönen farklı bir karar, ağ zaman aşımı gibi gerçek
    /// dünyada da olabilecek sebeplerle) bir sonraki provizyonu reddetmesini simüle eder.
    /// Gerçek bir PayCore entegrasyonunda bunun karşılığı yoktur; PayCore kararını kendi tarafında,
    /// bizim müdahalemiz olmadan verir.
    /// </summary>
    public bool ForceDeclineNextAuthorization { get; set; }

    private void InitializeSchema()
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();

        using (var cmd = new SqliteCommand(@"
            CREATE TABLE IF NOT EXISTS pcore_cards (
                paycore_card_reference TEXT PRIMARY KEY,
                masked_pan TEXT NOT NULL,
                card_holder_name TEXT NOT NULL,
                card_type INTEGER NOT NULL,
                status INTEGER NOT NULL DEFAULT 1,
                expiry_date TEXT NULL,
                created_date TEXT NOT NULL
            );", conn))
        {
            cmd.ExecuteNonQuery();
        }

        // PayCore kendi tarafında da kart limitini tutar (stand-in authorization için); banka
        // limit değişikliğini UpdateLimit ile buraya yansıtır. Eski paycore_mock.db dosyalarında
        // kolon yoktur; "duplicate column" hatası yoksayılarak eklenir.
        try
        {
            using var alterCmd = new SqliteCommand("ALTER TABLE pcore_cards ADD COLUMN card_limit TEXT NULL", conn);
            alterCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Kolon zaten mevcut.
        }

        using (var cmd = new SqliteCommand(@"
            CREATE TABLE IF NOT EXISTS pcore_authorizations (
                paycore_auth_reference TEXT PRIMARY KEY,
                paycore_card_reference TEXT NOT NULL,
                amount REAL NOT NULL,
                status INTEGER NOT NULL,          -- 1: Authorized, 2: Captured, 3: Voided, 4: Declined
                bank_reference_number TEXT NOT NULL,
                created_date TEXT NOT NULL,
                FOREIGN KEY(paycore_card_reference) REFERENCES pcore_cards(paycore_card_reference)
            );", conn))
        {
            cmd.ExecuteNonQuery();
        }
    }

    public PaycoreCardResult IssueCard(string maskedPan, string cardHolderName, CardType cardType)
    {
        string reference = "PCORE-CARD-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand(@"
            INSERT INTO pcore_cards (paycore_card_reference, masked_pan, card_holder_name, card_type, status, created_date)
            VALUES (@ref, @pan, @name, @type, 1, @date);", conn);
        cmd.Parameters.AddWithValue("@ref", reference);
        cmd.Parameters.AddWithValue("@pan", maskedPan);
        cmd.Parameters.AddWithValue("@name", cardHolderName);
        cmd.Parameters.AddWithValue("@type", (int)cardType);
        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();

        return new PaycoreCardResult { IsSuccess = true, PaycoreCardReference = reference };
    }

    public PaycoreResult SetCardStatus(string paycoreCardReference, CardStatus newStatus)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand("UPDATE pcore_cards SET status = @status WHERE paycore_card_reference = @ref", conn);
        cmd.Parameters.AddWithValue("@status", (int)newStatus);
        cmd.Parameters.AddWithValue("@ref", paycoreCardReference);
        int rows = cmd.ExecuteNonQuery();

        return rows > 0
            ? new PaycoreResult { IsSuccess = true }
            : new PaycoreResult { IsSuccess = false, ErrorMessage = "PayCore tarafında bu referansa ait kart bulunamadı." };
    }

    public PaycoreResult RenewCard(string paycoreCardReference, DateTime newExpiryDate)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand("UPDATE pcore_cards SET expiry_date = @exp WHERE paycore_card_reference = @ref", conn);
        cmd.Parameters.AddWithValue("@exp", newExpiryDate.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.Parameters.AddWithValue("@ref", paycoreCardReference);
        int rows = cmd.ExecuteNonQuery();

        return rows > 0
            ? new PaycoreResult { IsSuccess = true }
            : new PaycoreResult { IsSuccess = false, ErrorMessage = "PayCore tarafında bu referansa ait kart bulunamadı." };
    }

    public PaycoreResult UpdateLimit(string paycoreCardReference, decimal newLimit)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand("UPDATE pcore_cards SET card_limit = @limit WHERE paycore_card_reference = @ref", conn);
        cmd.Parameters.AddWithValue("@limit", newLimit.ToString("F2"));
        cmd.Parameters.AddWithValue("@ref", paycoreCardReference);
        int rows = cmd.ExecuteNonQuery();

        return rows > 0
            ? new PaycoreResult { IsSuccess = true }
            : new PaycoreResult { IsSuccess = false, ErrorMessage = "PayCore tarafında bu referansa ait kart bulunamadı." };
    }

    public PaycoreAuthResult Authorize(string paycoreCardReference, decimal amount, string bankReferenceNumber)
    {
        bool approve = !ForceDeclineNextAuthorization;
        ForceDeclineNextAuthorization = false; // tek seferlik bayrak

        string reference = "PCORE-AUTH-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant();

        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand(@"
            INSERT INTO pcore_authorizations (paycore_auth_reference, paycore_card_reference, amount, status, bank_reference_number, created_date)
            VALUES (@ref, @cardRef, @amount, @status, @bankRef, @date);", conn);
        cmd.Parameters.AddWithValue("@ref", reference);
        cmd.Parameters.AddWithValue("@cardRef", paycoreCardReference);
        cmd.Parameters.AddWithValue("@amount", (double)amount);
        cmd.Parameters.AddWithValue("@status", approve ? 1 : 4);
        cmd.Parameters.AddWithValue("@bankRef", bankReferenceNumber);
        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        cmd.ExecuteNonQuery();

        return approve
            ? new PaycoreAuthResult { IsApproved = true, PaycoreAuthReference = reference, ResponseCode = "00" }
            : new PaycoreAuthResult { IsApproved = false, PaycoreAuthReference = reference, ResponseCode = "05", ErrorMessage = "PayCore provizyonu reddetti (Do Not Honor)." };
    }

    public PaycoreResult Capture(string paycoreAuthReference)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand("UPDATE pcore_authorizations SET status = 2 WHERE paycore_auth_reference = @ref", conn);
        cmd.Parameters.AddWithValue("@ref", paycoreAuthReference);
        int rows = cmd.ExecuteNonQuery();

        return rows > 0
            ? new PaycoreResult { IsSuccess = true }
            : new PaycoreResult { IsSuccess = false, ErrorMessage = "PayCore tarafında bu referansa ait provizyon bulunamadı." };
    }

    public PaycoreResult Void(string paycoreAuthReference)
    {
        using var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using var cmd = new SqliteCommand("UPDATE pcore_authorizations SET status = 3 WHERE paycore_auth_reference = @ref", conn);
        cmd.Parameters.AddWithValue("@ref", paycoreAuthReference);
        int rows = cmd.ExecuteNonQuery();

        return rows > 0
            ? new PaycoreResult { IsSuccess = true }
            : new PaycoreResult { IsSuccess = false, ErrorMessage = "PayCore tarafında bu referansa ait provizyon bulunamadı." };
    }
}

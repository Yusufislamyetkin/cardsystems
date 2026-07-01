using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.Sqlite;

namespace BOA.Data.Providers;

/// <summary>
/// Projenin harici bir veritabanı (PostgreSQL/Oracle) kurulumu gerekmeden doğrudan çalışabilmesi için
/// geliştirilmiş SQLite Mock Veritabanı Sağlayıcısıdır.
/// Bu sınıf, yevmiye kayıtları, GL hesapları ve şifre doğrulama (HSM) gibi kurumsal (Enterprise) 
/// özellikleri SQLite üzerinde simüle eder.
/// </summary>
public class SqliteMockProvider : IBoaDbProvider
{
    private readonly string _connectionString;

    public SqliteMockProvider() : this("Data Source=boa_mock.db")
    {
    }

    /// <summary>
    /// Testlerin birbirinden izole, geçici bir veritabanı dosyası kullanabilmesi için eklenmiş
    /// aşırı yüklemedir (örn. "Data Source=" + Path.GetTempFileName()).
    /// </summary>
    public SqliteMockProvider(string connectionString)
    {
        _connectionString = connectionString;
        InitializeDatabase();
    }

    /// <summary>
    /// SQLite veritabanı şemasını (Tabloları) kurumsal standartlarda hazırlar.
    /// </summary>
    private void InitializeDatabase()
    {
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            // 1. Defter-i Kebir (GL) Hesaplar Tablosu
            string createAccountsTable = @"
                CREATE TABLE IF NOT EXISTS boa_accounts (
                    account_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    account_number TEXT UNIQUE NOT NULL,
                    account_name TEXT NOT NULL,
                    account_type INTEGER NOT NULL,
                    created_date TEXT NOT NULL
                );";

            // 1b. Müşteriler Tablosu (kartların bağlı olduğu gerçek kişi kaydı)
            string createCustomersTable = @"
                CREATE TABLE IF NOT EXISTS boa_customers (
                    customer_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    national_id TEXT UNIQUE NOT NULL,           -- T.C. Kimlik No
                    full_name TEXT NOT NULL,
                    phone TEXT NULL,
                    created_date TEXT NOT NULL
                );";

            // 1c. Müşteri Banka Hesapları Tablosu (vadesiz/kredi hesabı; kartlar buna bağlanır)
            string createBankAccountsTable = @"
                CREATE TABLE IF NOT EXISTS boa_bank_accounts (
                    bank_account_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    customer_id INTEGER NOT NULL,
                    account_number TEXT UNIQUE NOT NULL,
                    currency_code TEXT NOT NULL DEFAULT 'TRY',
                    account_type INTEGER NOT NULL,              -- 1: Vadesiz (Debit), 2: Kredi Karti Hesabi
                    created_date TEXT NOT NULL,
                    FOREIGN KEY(customer_id) REFERENCES boa_customers(customer_id)
                );";

            // 1d. BIN (Issuer Identification Number) Tablosu — gerçek bankacılıkta BKM'den güncellenen
            // BIN aralıkları tablosunun basitleştirilmiş karşılığı. Kart türüne göre BIN kodu artık
            // kaynak kodda sabit değil, buradan okunur.
            string createBinTable = @"
                CREATE TABLE IF NOT EXISTS boa_bin_table (
                    bin_code TEXT PRIMARY KEY,
                    card_type INTEGER NOT NULL,          -- 1: Debit, 2: Credit
                    card_brand TEXT NOT NULL
                );";

            // 2. Kartlar Tablosu (Maskeli PAN, Şifreli PAN, PIN Block, GL Hesap referansı barındırır)
            string createCardsTable = @"
                CREATE TABLE IF NOT EXISTS boa_cards (
                    card_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_number TEXT NOT NULL,                  -- Maskeli Kart No (435520******1234)
                    encrypted_pan TEXT NOT NULL,                 -- Şifreli Kart No
                    pin_hash TEXT NULL,                         -- HSM PIN Block
                    card_holder_name TEXT NOT NULL,
                    card_type INTEGER NOT NULL,
                    expiry_date TEXT NOT NULL,
                    status INTEGER NOT NULL DEFAULT 1,
                    card_limit REAL NOT NULL DEFAULT 0.00,
                    balance REAL NOT NULL DEFAULT 0.00,         -- Önbellek Bakiye
                    account_id INTEGER NOT NULL,                -- GL Hesabı
                    customer_id INTEGER NOT NULL,               -- Kart hamilinin müşteri kaydı
                    bank_account_id INTEGER NOT NULL,           -- Bağlı vadesiz/kredi hesabı
                    created_date TEXT NOT NULL,
                    FOREIGN KEY(account_id) REFERENCES boa_accounts(account_id),
                    FOREIGN KEY(customer_id) REFERENCES boa_customers(customer_id),
                    FOREIGN KEY(bank_account_id) REFERENCES boa_bank_accounts(bank_account_id)
                );";

            // 3. Çift Kayıt Yevmiye Tablosu
            string createLedgerTable = @"
                CREATE TABLE IF NOT EXISTS boa_ledger_entries (
                    entry_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    account_id INTEGER NOT NULL,
                    debit_amount REAL NOT NULL DEFAULT 0.00,
                    credit_amount REAL NOT NULL DEFAULT 0.00,
                    reference_number TEXT NOT NULL,
                    transaction_type INTEGER NULL,              -- TransactionType enum degeri (Refund/Reversal ayrimi icin)
                    merchant_id TEXT NULL,
                    mcc TEXT NULL,
                    created_date TEXT NOT NULL,
                    FOREIGN KEY(account_id) REFERENCES boa_accounts(account_id)
                );";

            // 3b. Provizyon (Authorization) Tablosu — Authorize/Capture/Void akışının kalbi.
            // Provizyon alındığında burada bir "hold" kaydı oluşur; yevmiye defterine hiçbir şey yazılmaz.
            // Kesinleşme (Capture) ancak ayrı bir adımda gerçekleşir.
            string createAuthorizationsTable = @"
                CREATE TABLE IF NOT EXISTS boa_authorizations (
                    authorization_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_id INTEGER NOT NULL,
                    transaction_type INTEGER NOT NULL,
                    amount REAL NOT NULL,
                    response_code INTEGER NOT NULL,             -- ISO 8583 DE39 benzeri (00,05,14,51,54,55,96)
                    authorization_code TEXT NULL,               -- Onaylandıysa 6 haneli alfanumerik kod
                    status INTEGER NOT NULL,                    -- 1: Authorized, 2: Captured, 3: Voided, 4: Declined
                    description TEXT NOT NULL,
                    reference_number TEXT NOT NULL,
                    merchant_id TEXT NULL,
                    mcc TEXT NULL,
                    user_id TEXT NOT NULL,
                    channel TEXT NOT NULL,
                    client_ip TEXT NOT NULL,
                    created_date TEXT NOT NULL,
                    captured_date TEXT NULL,
                    FOREIGN KEY(card_id) REFERENCES boa_cards(card_id)
                );";

            // 3c. Hesap Kesimi (Statement) Tablosu — Gün sonu (EOD) batch sürecinin ürettiği ekstreler.
            string createStatementsTable = @"
                CREATE TABLE IF NOT EXISTS boa_statements (
                    statement_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_id INTEGER NOT NULL,
                    statement_date TEXT NOT NULL,
                    due_date TEXT NOT NULL,
                    total_debt REAL NOT NULL,
                    minimum_payment REAL NOT NULL,
                    is_paid INTEGER NOT NULL DEFAULT 0,
                    interest_applied INTEGER NOT NULL DEFAULT 0,
                    created_date TEXT NOT NULL,
                    FOREIGN KEY(card_id) REFERENCES boa_cards(card_id)
                );";

            // 4. Denetim (Audit) tablosu
            string createAuditTable = @"
                CREATE TABLE IF NOT EXISTS boa_card_audit_log (
                    audit_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_id INTEGER NOT NULL,
                    operation_type TEXT NOT NULL,
                    old_value TEXT,
                    new_value TEXT,
                    reason TEXT,
                    user_id TEXT NOT NULL,
                    channel TEXT NOT NULL,
                    client_ip TEXT NOT NULL,
                    log_date TEXT NOT NULL,
                    FOREIGN KEY(card_id) REFERENCES boa_cards(card_id)
                );";

            using (var cmd = new SqliteCommand(createCustomersTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createBankAccountsTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createBinTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createAccountsTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createCardsTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createLedgerTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createAuthorizationsTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createStatementsTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createAuditTable, connection)) { cmd.ExecuteNonQuery(); }

            using (var cmd = new SqliteCommand(
                "INSERT OR IGNORE INTO boa_bin_table (bin_code, card_type, card_brand) VALUES ('435520', 1, 'VISA'), ('543789', 2, 'MASTERCARD');", connection))
            {
                cmd.ExecuteNonQuery();
            }
        }
    }

    // Kart satırlarını, bağlı olduğu müşterinin T.C. Kimlik No'suyla birlikte döndüren ortak sorgu.
    private const string CardWithCustomerSelect =
        "SELECT c.*, cu.national_id AS national_id FROM boa_cards c JOIN boa_customers cu ON c.customer_id = cu.customer_id";

    /// <summary>
    /// Stored Procedure adını kontrol ederek, SQLite dilinde eşdeğer SELECT işlemlerini simüle eder.
    /// </summary>
    public DataTable ExecuteStoredProcedureReader(string spName, Dictionary<string, object> parameters)
    {
        var dt = new DataTable();

        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();

            switch (spName.ToLowerInvariant())
            {
                case "sp_boa_card_create":
                    // 1. Yeni Kart Tanımlama Simülasyonu
                    ExecuteInsertCard(connection, parameters);

                    var selectNewCardCmd = new SqliteCommand(CardWithCustomerSelect + " ORDER BY c.card_id DESC LIMIT 1", connection);
                    using (var reader = selectNewCardCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_get_list":
                    // 2. Kartları Listeleme Simülasyonu
                    string query = CardWithCustomerSelect + " WHERE 1=1";
                    var listCmd = new SqliteCommand();
                    listCmd.Connection = connection;

                    if (parameters.TryGetValue("p_holder_name", out var holderName) && holderName != null && !string.IsNullOrEmpty(holderName.ToString()))
                    {
                        query += " AND c.card_holder_name LIKE @holder";
                        listCmd.Parameters.AddWithValue("@holder", $"%{holderName}%");
                    }
                    if (parameters.TryGetValue("p_card_type", out var cardType) && cardType != null)
                    {
                        query += " AND c.card_type = @cardType";
                        listCmd.Parameters.AddWithValue("@cardType", cardType);
                    }
                    if (parameters.TryGetValue("p_status", out var status) && status != null)
                    {
                        query += " AND c.status = @status";
                        listCmd.Parameters.AddWithValue("@status", status);
                    }
                    if (parameters.TryGetValue("p_card_id", out var cardIdFilter) && cardIdFilter != null)
                    {
                        query += " AND c.card_id = @cardIdFilter";
                        listCmd.Parameters.AddWithValue("@cardIdFilter", cardIdFilter);
                    }
                    query += " ORDER BY c.card_id DESC";
                    listCmd.CommandText = query;

                    using (var reader = listCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_update_limit":
                    // 3. Kart Limiti Güncelleme Simülasyonu
                    ExecuteUpdateLimit(connection, parameters);

                    var selectLimitCardCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                    selectLimitCardCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectLimitCardCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_set_status":
                    // 4. Kart Durumu Güncelleme Simülasyonu
                    ExecuteSetStatus(connection, parameters);

                    var selectStatusCardCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                    selectStatusCardCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectStatusCardCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_renew":
                    // 16. Kart Yenileme (Son Kullanma Tarihini Uzatma) — Gün Sonu Batch Sürecinin bir parçası
                    ExecuteRenewCard(connection, parameters);

                    var selectRenewedCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                    selectRenewedCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectRenewedCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_create_transaction":
                    // 5. Kart Hareketi Ekleme Simülasyonu (Çift Kayıt Muhasebe ile)
                    int newEntryId = ExecuteCreateTransaction(connection, parameters);

                    // Yeni eklenen hareketi döndür
                    var selectTransCmd = new SqliteCommand(@"
                        SELECT entry_id AS transaction_id,
                               @cardId AS card_id,
                               @type AS transaction_type,
                               CASE WHEN debit_amount > 0 THEN debit_amount ELSE credit_amount END AS amount,
                               @desc AS description,
                               created_date AS transaction_date,
                               reference_number,
                               merchant_id,
                               mcc
                        FROM boa_ledger_entries
                        WHERE entry_id = @entryId", connection);
                    selectTransCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    selectTransCmd.Parameters.AddWithValue("@type", parameters["p_transaction_type"]);
                    selectTransCmd.Parameters.AddWithValue("@desc", parameters["p_description"]);
                    selectTransCmd.Parameters.AddWithValue("@entryId", newEntryId);
                    using (var reader = selectTransCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_get_transactions":
                    // 6. Kart Hareketlerini Listeleme Simülasyonu (Yevmiye Kayıtlarından)
                    var transListCmd = new SqliteCommand(@"
                        SELECT
                            entry_id AS transaction_id,
                            @cardId AS card_id,
                            COALESCE(transaction_type, CASE
                                WHEN debit_amount > 0 AND reference_number <> 'INITIAL_FUND' THEN 1
                                WHEN credit_amount > 0 AND reference_number = 'INITIAL_FUND' THEN 3
                                ELSE 3
                            END) AS transaction_type,
                            CASE WHEN debit_amount > 0 THEN debit_amount ELSE credit_amount END AS amount,
                            CASE WHEN debit_amount > 0 THEN 'BORC - Kartli Harcama' ELSE 'ALACAK - Para Yatirma/Odeme' END AS description,
                            created_date AS transaction_date,
                            reference_number,
                            merchant_id,
                            mcc
                        FROM boa_ledger_entries
                        WHERE account_id = (SELECT account_id FROM boa_cards WHERE card_id = @cardId)
                        ORDER BY entry_id DESC", connection);
                    transListCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = transListCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_get_secure_details":
                    // 7. Şifreli Kart ve PIN Detaylarını Çekme Simülasyonu
                    var selectSecureCmd = new SqliteCommand("SELECT card_id, card_number, encrypted_pan, pin_hash FROM boa_cards WHERE card_id = @cardId", connection);
                    selectSecureCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectSecureCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_bin_lookup":
                    // 8. Kart türüne göre BIN kodu sorgulama (BKM BIN tablosu simülasyonu)
                    var binCmd = new SqliteCommand("SELECT bin_code, card_brand FROM boa_bin_table WHERE card_type = @cardType LIMIT 1", connection);
                    binCmd.Parameters.AddWithValue("@cardType", parameters["p_card_type"]);
                    using (var reader = binCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_auth_create":
                    // 9. Provizyon (Authorization/Hold) Oluşturma
                    int newAuthId = ExecuteCreateAuthorization(connection, parameters);
                    var selectAuthCmd = new SqliteCommand("SELECT * FROM boa_authorizations WHERE authorization_id = @id", connection);
                    selectAuthCmd.Parameters.AddWithValue("@id", newAuthId);
                    using (var reader = selectAuthCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_auth_capture":
                    // 10. Provizyonu Kesinleştirme (Capture) — yevmiye defterine gerçek kayıt atar
                    ExecuteCaptureAuthorization(connection, parameters);
                    var selectCapturedCmd = new SqliteCommand("SELECT * FROM boa_authorizations WHERE authorization_id = @id", connection);
                    selectCapturedCmd.Parameters.AddWithValue("@id", parameters["p_authorization_id"]);
                    using (var reader = selectCapturedCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_auth_void":
                    // 11. Provizyonu İptal Etme (Void) — hiçbir muhasebe kaydı oluşturmaz
                    ExecuteVoidAuthorization(connection, parameters);
                    var selectVoidedCmd = new SqliteCommand("SELECT * FROM boa_authorizations WHERE authorization_id = @id", connection);
                    selectVoidedCmd.Parameters.AddWithValue("@id", parameters["p_authorization_id"]);
                    using (var reader = selectVoidedCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_statement_create":
                    // 12. Hesap Kesimi (Ekstre) Oluşturma
                    int newStatementId;
                    using (var cmd = new SqliteCommand(@"
                        INSERT INTO boa_statements (card_id, statement_date, due_date, total_debt, minimum_payment, is_paid, interest_applied, created_date)
                        VALUES (@cardId, @stmtDate, @dueDate, @debt, @minPay, 0, 0, @created);
                        SELECT last_insert_rowid();", connection))
                    {
                        cmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                        cmd.Parameters.AddWithValue("@stmtDate", Convert.ToDateTime(parameters["p_statement_date"]).ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@dueDate", Convert.ToDateTime(parameters["p_due_date"]).ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@debt", Convert.ToDouble(parameters["p_total_debt"]));
                        cmd.Parameters.AddWithValue("@minPay", Convert.ToDouble(parameters["p_minimum_payment"]));
                        cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        newStatementId = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                    using (var cmd = new SqliteCommand("SELECT * FROM boa_statements WHERE statement_id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", newStatementId);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_statement_get_open":
                    // 13. Bir Kartın Ödenmemiş (Açık) Son Ekstresini Sorgulama
                    using (var cmd = new SqliteCommand(
                        "SELECT * FROM boa_statements WHERE card_id = @cardId AND is_paid = 0 ORDER BY statement_id DESC LIMIT 1", connection))
                    {
                        cmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_statement_get_list":
                    // 14. Bir Karta Ait Tüm Ekstreleri Listeleme
                    using (var cmd = new SqliteCommand(
                        "SELECT * FROM boa_statements WHERE card_id = @cardId ORDER BY statement_id DESC", connection))
                    {
                        cmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_statement_mark_interest_applied":
                    // 15. Ekstreye Gecikme Faizinin İşlendiğini Kaydetme (Tekrar Çalıştırmada Çift Faiz Önlemi)
                    using (var cmd = new SqliteCommand(
                        "UPDATE boa_statements SET interest_applied = 1 WHERE statement_id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", parameters["p_statement_id"]);
                        cmd.ExecuteNonQuery();
                    }
                    break;

                default:
                    throw new NotImplementedException($"Mock veritabanında '{spName}' prosedürü tanımlı değil.");
            }
        }

        // Oracle / PostgreSQL uyumluluğu için kolon isimlerini küçük harfe dönüştür
        foreach (DataColumn col in dt.Columns)
        {
            col.ColumnName = col.ColumnName.ToLowerInvariant();
        }

        return dt;
    }

    // DataTable.Load(reader) yerine kullanılır: Load, kaynak tablonun UNIQUE kısıtlarını (örn. national_id)
    // DataTable üzerinde de kurar ve bir müşterinin birden fazla kartı olduğu JOIN sorgularında
    // (aynı national_id birden çok satırda tekrar ettiği için) sahte bir constraint ihlaline yol açar.
    private static void LoadReaderIntoTable(DataTable dt, IDataReader reader)
    {
        for (int i = 0; i < reader.FieldCount; i++)
        {
            dt.Columns.Add(reader.GetName(i), reader.GetFieldType(i));
        }

        while (reader.Read())
        {
            var row = dt.NewRow();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? DBNull.Value : reader.GetValue(i);
            }
            dt.Rows.Add(row);
        }
    }

    public int ExecuteStoredProcedureNonQuery(string spName, Dictionary<string, object> parameters)
    {
        ExecuteStoredProcedureReader(spName, parameters);
        return 1;
    }

    // =====================================================================================
    // SQLite YARDIMCI İŞLEMLERİ (ÇİFT KAYIT VE LOCKING SİMÜLASYONU)
    // =====================================================================================

    private void ExecuteInsertCard(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        // SQLite'ta transaction başlatarak ACID bütünlüğünü koruyoruz
        using var tx = conn.BeginTransaction();
        try
        {
            string nationalId = parameters.TryGetValue("p_national_id", out var nid) ? nid?.ToString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(nationalId))
                throw new ArgumentException("Müşteri T.C. Kimlik No (p_national_id) belirtilmeden kart açılamaz.");
            string phone = parameters.TryGetValue("p_phone", out var ph) ? ph?.ToString() ?? "" : "";

            // A0. Müşteriyi T.C. Kimlik No'ya göre bul, yoksa oluştur.
            int customerId;
            using (var cmd = new SqliteCommand("SELECT customer_id FROM boa_customers WHERE national_id = @nid", conn, tx))
            {
                cmd.Parameters.AddWithValue("@nid", nationalId);
                var existing = cmd.ExecuteScalar();
                if (existing != null)
                {
                    customerId = Convert.ToInt32(existing);
                }
                else
                {
                    using var insCust = new SqliteCommand(
                        "INSERT INTO boa_customers (national_id, full_name, phone, created_date) VALUES (@nid, @name, @phone, @date); SELECT last_insert_rowid();", conn, tx);
                    insCust.Parameters.AddWithValue("@nid", nationalId);
                    insCust.Parameters.AddWithValue("@name", parameters["p_card_holder_name"]);
                    insCust.Parameters.AddWithValue("@phone", string.IsNullOrWhiteSpace(phone) ? (object)DBNull.Value : phone);
                    insCust.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    customerId = Convert.ToInt32(insCust.ExecuteScalar());
                }
            }

            // A1. Karta bağlı vadesiz/kredi hesabını aç. Hesap no, satırın kendi (biricik) PK'sinden
            // türetilir; zaman damgası tabanlı üretim aynı anda gelen isteklerde çakışabilirdi.
            int bankAccountId;
            using (var cmd = new SqliteCommand(
                "INSERT INTO boa_bank_accounts (customer_id, account_number, currency_code, account_type, created_date) VALUES (@custId, @accNum, 'TRY', @accType, @date); SELECT last_insert_rowid();", conn, tx))
            {
                cmd.Parameters.AddWithValue("@custId", customerId);
                cmd.Parameters.AddWithValue("@accNum", Guid.NewGuid().ToString("N"));
                cmd.Parameters.AddWithValue("@accType", parameters["p_card_type"]);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                bankAccountId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            using (var cmd = new SqliteCommand("UPDATE boa_bank_accounts SET account_number = @accNum WHERE bank_account_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@accNum", "TR" + bankAccountId.ToString("D22"));
                cmd.Parameters.AddWithValue("@id", bankAccountId);
                cmd.ExecuteNonQuery();
            }

            // A. Karta Ait GL Muhasebe Hesabı Aç
            string maskedCardNo = parameters["p_card_number"].ToString() ?? "";
            string accNum = "GL-CARD-" + DateTime.Now.ToString("yyyyMMdd") + "-" + maskedCardNo.Substring(Math.Max(0, maskedCardNo.Length - 4));
            string accSql = "INSERT INTO boa_accounts (account_number, account_name, account_type, created_date) VALUES (@accNum, @name, 1, @date); SELECT last_insert_rowid();";
            int accId = 0;
            using (var cmd = new SqliteCommand(accSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@accNum", accNum);
                cmd.Parameters.AddWithValue("@name", parameters["p_card_holder_name"].ToString() + " Kart Hesabi");
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                accId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            // B. Kart Bilgilerini Ekle
            string insertSql = @"
                INSERT INTO boa_cards (card_number, encrypted_pan, pin_hash, card_holder_name, card_type, expiry_date, status, card_limit, balance, account_id, customer_id, bank_account_id, created_date)
                VALUES (@num, @enc, @pin, @name, @type, @expiry, 1, @limit, @balance, @accId, @custId, @bankAccId, @created); SELECT last_insert_rowid();";

            int cardId = 0;
            using (var cmd = new SqliteCommand(insertSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@num", parameters["p_card_number"]);
                cmd.Parameters.AddWithValue("@enc", parameters["p_encrypted_pan"]);
                cmd.Parameters.AddWithValue("@pin", parameters["p_pin_hash"] ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@name", parameters["p_card_holder_name"]);
                cmd.Parameters.AddWithValue("@type", parameters["p_card_type"]);
                cmd.Parameters.AddWithValue("@expiry", Convert.ToDateTime(parameters["p_expiry_date"]).ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@limit", Convert.ToDouble(parameters["p_limit"]));
                cmd.Parameters.AddWithValue("@balance", Convert.ToDouble(parameters["p_initial_balance"]));
                cmd.Parameters.AddWithValue("@accId", accId);
                cmd.Parameters.AddWithValue("@custId", customerId);
                cmd.Parameters.AddWithValue("@bankAccId", bankAccountId);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cardId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            // C. İlk Bakiye Tanımlaması Varsa Çift Kayıt Yevmiye Girişi Yap (Credit)
            double initialBalance = Convert.ToDouble(parameters["p_initial_balance"]);
            if (initialBalance > 0)
            {
                string ledgerSql = "INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number, created_date) VALUES (@accId, 0.00, @balance, 'INITIAL_FUND', @date);";
                using (var cmd = new SqliteCommand(ledgerSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@accId", accId);
                    cmd.Parameters.AddWithValue("@balance", initialBalance);
                    cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.ExecuteNonQuery();
                }
            }

            // D. Audit Log
            string auditSql = @"
                INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip, log_date)
                VALUES (@cardId, 'NEW_CARD', NULL, @newVal, 'Kart ve GL Hesap Acilisi (SQLite)', @user, @channel, @ip, @date);";
            
            using (var cmd = new SqliteCommand(auditSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.Parameters.AddWithValue("@newVal", parameters["p_card_number"]);
                cmd.Parameters.AddWithValue("@user", parameters["p_user_id"]);
                cmd.Parameters.AddWithValue("@channel", parameters["p_channel"]);
                cmd.Parameters.AddWithValue("@ip", parameters["p_client_ip"]);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private void ExecuteUpdateLimit(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            double newLimit = Convert.ToDouble(parameters["p_new_limit"]);

            // Pessimistic Lock simülasyonu için kartı ve eski limiti çek
            double oldLimit = 0.00;
            using (var cmd = new SqliteCommand("SELECT card_limit FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                oldLimit = Convert.ToDouble(cmd.ExecuteScalar());
            }

            // Limiti güncelle
            using (var cmd = new SqliteCommand("UPDATE boa_cards SET card_limit = @limit WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@limit", newLimit);
                cmd.Parameters.AddWithValue("@id", cardId);
                cmd.ExecuteNonQuery();
            }

            // Audit log
            string auditSql = @"
                INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip, log_date)
                VALUES (@cardId, 'LIMIT_UPDATE', @oldVal, @newVal, 'Limit Guncelleme (SQLite)', @user, @channel, @ip, @date);";

            using (var cmd = new SqliteCommand(auditSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.Parameters.AddWithValue("@oldVal", oldLimit.ToString());
                cmd.Parameters.AddWithValue("@newVal", newLimit.ToString());
                cmd.Parameters.AddWithValue("@user", parameters["p_user_id"]);
                cmd.Parameters.AddWithValue("@channel", parameters["p_channel"]);
                cmd.Parameters.AddWithValue("@ip", parameters["p_client_ip"]);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private void ExecuteSetStatus(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            int newStatus = Convert.ToInt32(parameters["p_new_status"]);
            string reason = parameters["p_reason"].ToString() ?? string.Empty;

            int oldStatus = 1;
            using (var cmd = new SqliteCommand("SELECT status FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                oldStatus = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using (var cmd = new SqliteCommand("UPDATE boa_cards SET status = @status WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@status", newStatus);
                cmd.Parameters.AddWithValue("@id", cardId);
                cmd.ExecuteNonQuery();
            }

            string auditSql = @"
                INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip, log_date)
                VALUES (@cardId, 'STATUS_CHANGE', @oldVal, @newVal, @reason, @user, @channel, @ip, @date);";

            using (var cmd = new SqliteCommand(auditSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.Parameters.AddWithValue("@oldVal", oldStatus.ToString());
                cmd.Parameters.AddWithValue("@newVal", newStatus.ToString());
                cmd.Parameters.AddWithValue("@reason", reason);
                cmd.Parameters.AddWithValue("@user", parameters["p_user_id"]);
                cmd.Parameters.AddWithValue("@channel", parameters["p_channel"]);
                cmd.Parameters.AddWithValue("@ip", parameters["p_client_ip"]);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Son kullanma tarihi yaklaşan bir kartı yeniler (expiry_date'i uzatır). Gerçek bir bankada bu
    /// süreç yeni bir fiziksel kart basımı/gönderimini de tetikler; bu proje yalnızca son kullanma
    /// tarihini günceller ve denetim kaydı bırakır.
    /// </summary>
    private void ExecuteRenewCard(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            DateTime newExpiry = Convert.ToDateTime(parameters["p_new_expiry_date"]);

            string oldExpiry;
            using (var cmd = new SqliteCommand("SELECT expiry_date FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                oldExpiry = cmd.ExecuteScalar()?.ToString() ?? string.Empty;
            }

            using (var cmd = new SqliteCommand("UPDATE boa_cards SET expiry_date = @expiry WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@expiry", newExpiry.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@id", cardId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqliteCommand(@"
                INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip, log_date)
                VALUES (@cardId, 'CARD_RENEWED', @oldVal, @newVal, 'Son kullanma tarihi yaklaştığı için otomatik yenileme (EOD Batch)', @user, @channel, @ip, @date);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.Parameters.AddWithValue("@oldVal", oldExpiry);
                cmd.Parameters.AddWithValue("@newVal", newExpiry.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@user", parameters["p_user_id"]);
                cmd.Parameters.AddWithValue("@channel", parameters["p_channel"]);
                cmd.Parameters.AddWithValue("@ip", parameters["p_client_ip"]);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private int ExecuteCreateTransaction(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            int transType = Convert.ToInt32(parameters["p_transaction_type"]);
            double amount = Convert.ToDouble(parameters["p_amount"]);
            string refNo = parameters["p_reference_number"].ToString() ?? string.Empty;
            object merchantId = parameters.TryGetValue("p_merchant_id", out var mid) && mid != null ? mid : DBNull.Value;
            object mcc = parameters.TryGetValue("p_mcc", out var mccVal) && mccVal != null ? mccVal : DBNull.Value;

            int accId = 0;
            int cardType = 1;
            int status = 1;
            double limit = 0.00;

            // Kartı kilitle ve bilgilerini al
            using (var cmd = new SqliteCommand("SELECT account_id, card_type, status, card_limit FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                using (var r = cmd.ExecuteReader())
                {
                    if (r.Read())
                    {
                        accId = r.GetInt32(0);
                        cardType = r.GetInt32(1);
                        status = r.GetInt32(2);
                        limit = r.GetDouble(3);
                    }
                    else
                    {
                        throw new Exception("Kart Bulunamadi!");
                    }
                }
            }

            if (status != 1)
            {
                throw new Exception("İşlem Reddedildi: Kart aktif durumda değil!");
            }

            // Defter kayıtlarından güncel bakiyeyi hesapla
            double curBalance = 0.00;
            using (var cmd = new SqliteCommand("SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) FROM boa_ledger_entries WHERE account_id = @accId", conn, tx))
            {
                cmd.Parameters.AddWithValue("@accId", accId);
                curBalance = Convert.ToDouble(cmd.ExecuteScalar());
            }

            // Finansal Bakiye / Limit kontrolleri
            // Not: Ücret/Faiz (Fee) yansıtması banka tarafından zorlanan bir kayıttır; müşteri işlemi
            // (Harcama/Çekim) gibi bakiye/limit kontrolüne tabi değildir — gecikme faizi kartı zaten
            // limit üzerine taşıyabilir, bu gerçek bankacılıkta da böyledir.
            if (transType == 1 || transType == 2 || transType == 4) // Harcama / Çekim / Ücret-Faiz (Fee)
            {
                if (transType != 4)
                {
                    if (cardType == 1 && curBalance < amount) // Banka Kartı Bakiye kontrolü
                    {
                        throw new Exception($"Yetersiz hesap bakiyesi! Kullanılabilir bakiye: {curBalance} TL");
                    }
                    // Kredi kartlarında borç, çift kayıt muhasebe modelinde NEGATİF bakiye olarak tutulur
                    // (harcama = borç artışı = negatife doğru). Bu yüzden kullanılabilir limit = limit + bakiye'dir
                    // (limit - bakiye DEĞİL — bu ters formül limiti pratikte hiç kontrol etmiyordu).
                    else if (cardType == 2 && (limit + curBalance) < amount) // Kredi Kartı Limit kontrolü
                    {
                        throw new Exception($"Yetersiz kart limiti! Kullanılabilir limit: {limit + curBalance} TL");
                    }
                }

                // Borç Kaydı Girişi (Debit)
                string ledgerSql = "INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc, created_date) VALUES (@accId, @amount, 0.00, @refNo, @transType, @merchantId, @mcc, @date); SELECT last_insert_rowid();";
                int entryId = 0;
                using (var cmd = new SqliteCommand(ledgerSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@accId", accId);
                    cmd.Parameters.AddWithValue("@amount", amount);
                    cmd.Parameters.AddWithValue("@refNo", refNo);
                    cmd.Parameters.AddWithValue("@transType", transType);
                    cmd.Parameters.AddWithValue("@merchantId", merchantId);
                    cmd.Parameters.AddWithValue("@mcc", mcc);
                    cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    entryId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // Önbellek (cached) bakiye güncelle
                using (var cmd = new SqliteCommand("UPDATE boa_cards SET balance = (SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) FROM boa_ledger_entries WHERE account_id = @accId) WHERE card_id = @cardId", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@accId", accId);
                    cmd.Parameters.AddWithValue("@cardId", cardId);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return entryId;
            }
            else if (transType == 3) // Yatırma / Ödeme
            {
                // Alacak Kaydı Girişi (Credit)
                string ledgerSql = "INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc, created_date) VALUES (@accId, 0.00, @amount, @refNo, @transType, @merchantId, @mcc, @date); SELECT last_insert_rowid();";
                int entryId = 0;
                using (var cmd = new SqliteCommand(ledgerSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@accId", accId);
                    cmd.Parameters.AddWithValue("@amount", amount);
                    cmd.Parameters.AddWithValue("@refNo", refNo);
                    cmd.Parameters.AddWithValue("@transType", transType);
                    cmd.Parameters.AddWithValue("@merchantId", merchantId);
                    cmd.Parameters.AddWithValue("@mcc", mcc);
                    cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                    entryId = Convert.ToInt32(cmd.ExecuteScalar());
                }

                // Önbellek bakiye güncelle
                using (var cmd = new SqliteCommand("UPDATE boa_cards SET balance = (SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) FROM boa_ledger_entries WHERE account_id = @accId) WHERE card_id = @cardId", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@accId", accId);
                    cmd.Parameters.AddWithValue("@cardId", cardId);
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return entryId;
            }
            else
            {
                throw new NotSupportedException("Desteklenmeyen işlem türü!");
            }
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Provizyon (authorization/hold) oluşturur. p_forced_response_code verilmişse (örn. PIN hatası
    /// nedeniyle 55) bakiye/limit kontrolü hiç yapılmadan doğrudan reddedilir; aksi halde kart durumu,
    /// son kullanma tarihi ve (mevcut bloke edilmiş provizyonlar da dahil edilerek) bakiye/limit kontrol edilir.
    /// </summary>
    private int ExecuteCreateAuthorization(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            int transType = Convert.ToInt32(parameters["p_transaction_type"]);
            double amount = Convert.ToDouble(parameters["p_amount"]);
            string description = parameters["p_description"].ToString() ?? string.Empty;
            string refNo = parameters["p_reference_number"].ToString() ?? string.Empty;
            object merchantId = parameters.TryGetValue("p_merchant_id", out var mid) && mid != null ? mid : DBNull.Value;
            object mcc = parameters.TryGetValue("p_mcc", out var mccVal) && mccVal != null ? mccVal : DBNull.Value;
            string candidateAuthCode = parameters["p_candidate_auth_code"].ToString() ?? string.Empty;
            int? forcedResponseCode = parameters.TryGetValue("p_forced_response_code", out var frc) && frc != null && frc != DBNull.Value ? Convert.ToInt32(frc) : (int?)null;

            int cardType = 1, status = 1;
            double limit = 0.00;
            DateTime expiryDate;

            using (var cmd = new SqliteCommand("SELECT card_type, status, card_limit, expiry_date FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new Exception("Kart Bulunamadi!");
                cardType = r.GetInt32(0);
                status = r.GetInt32(1);
                limit = r.GetDouble(2);
                expiryDate = DateTime.Parse(r.GetString(3));
            }

            int responseCode;
            int authStatus;
            string? finalAuthCode;

            if (forcedResponseCode.HasValue)
            {
                responseCode = forcedResponseCode.Value;
                authStatus = 4; // Declined
                finalAuthCode = null;
            }
            else if (status != 1)
            {
                responseCode = 5; // Do Not Honor
                authStatus = 4;
                finalAuthCode = null;
            }
            else if (expiryDate < DateTime.Now)
            {
                responseCode = 54; // Expired Card
                authStatus = 4;
                finalAuthCode = null;
            }
            else
            {
                double curBalance;
                using (var cmd = new SqliteCommand(
                    "SELECT COALESCE(SUM(l.credit_amount) - SUM(l.debit_amount), 0.00) FROM boa_ledger_entries l JOIN boa_cards c ON l.account_id = c.account_id WHERE c.card_id = @id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", cardId);
                    curBalance = Convert.ToDouble(cmd.ExecuteScalar());
                }

                double held;
                using (var cmd = new SqliteCommand(
                    "SELECT COALESCE(SUM(amount), 0.00) FROM boa_authorizations WHERE card_id = @id AND status = 1", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", cardId);
                    held = Convert.ToDouble(cmd.ExecuteScalar());
                }

                // Kredi kartında borç negatif bakiye olarak tutulur; kullanılabilir limit = limit + bakiye - bloke.
                bool insufficient = (cardType == 1 && (curBalance - held) < amount) ||
                                     (cardType == 2 && (limit + curBalance - held) < amount);

                if (insufficient)
                {
                    responseCode = 51; // Insufficient Funds
                    authStatus = 4;
                    finalAuthCode = null;
                }
                else
                {
                    responseCode = 0; // Approved
                    authStatus = 1; // Authorized (hold)
                    finalAuthCode = candidateAuthCode;
                }
            }

            int newAuthId;
            using (var cmd = new SqliteCommand(@"
                INSERT INTO boa_authorizations (card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, user_id, channel, client_ip, created_date)
                VALUES (@cardId, @transType, @amount, @responseCode, @authCode, @authStatus, @desc, @refNo, @merchantId, @mcc, @user, @channel, @ip, @date);
                SELECT last_insert_rowid();", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.Parameters.AddWithValue("@transType", transType);
                cmd.Parameters.AddWithValue("@amount", amount);
                cmd.Parameters.AddWithValue("@responseCode", responseCode);
                cmd.Parameters.AddWithValue("@authCode", (object?)finalAuthCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@authStatus", authStatus);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@refNo", refNo);
                cmd.Parameters.AddWithValue("@merchantId", merchantId);
                cmd.Parameters.AddWithValue("@mcc", mcc);
                cmd.Parameters.AddWithValue("@user", parameters["p_user_id"]);
                cmd.Parameters.AddWithValue("@channel", parameters["p_channel"]);
                cmd.Parameters.AddWithValue("@ip", parameters["p_client_ip"]);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                newAuthId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            tx.Commit();
            return newAuthId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Authorized durumundaki bir provizyonu kesinleştirir: yevmiye defterine gerçek borç/alacak
    /// kaydını yazar, kart önbellek bakiyesini günceller ve provizyonu Captured olarak işaretler.
    /// </summary>
    private void ExecuteCaptureAuthorization(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int authId = Convert.ToInt32(parameters["p_authorization_id"]);

            int cardId, transType, status;
            double amount;
            string description, refNo;
            object merchantId, mcc;

            using (var cmd = new SqliteCommand(
                "SELECT card_id, transaction_type, amount, status, description, reference_number, merchant_id, mcc FROM boa_authorizations WHERE authorization_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", authId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new Exception("Provizyon bulunamadi!");
                cardId = r.GetInt32(0);
                transType = r.GetInt32(1);
                amount = r.GetDouble(2);
                status = r.GetInt32(3);
                description = r.GetString(4);
                refNo = r.GetString(5);
                merchantId = r.IsDBNull(6) ? DBNull.Value : r.GetString(6);
                mcc = r.IsDBNull(7) ? DBNull.Value : r.GetString(7);
            }

            if (status != 1)
            {
                throw new Exception("Bu provizyon Authorized durumda değil, kesinleştirilemez (zaten Captured/Voided/Declined olabilir).");
            }

            int accId;
            using (var cmd = new SqliteCommand("SELECT account_id FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                accId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            bool isDebitEntry = transType == 1 || transType == 2;
            using (var cmd = new SqliteCommand(
                "INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc, created_date) VALUES (@accId, @debit, @credit, @refNo, @transType, @merchantId, @mcc, @date);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@accId", accId);
                cmd.Parameters.AddWithValue("@debit", isDebitEntry ? amount : 0.00);
                cmd.Parameters.AddWithValue("@credit", isDebitEntry ? 0.00 : amount);
                cmd.Parameters.AddWithValue("@refNo", refNo);
                cmd.Parameters.AddWithValue("@transType", transType);
                cmd.Parameters.AddWithValue("@merchantId", merchantId);
                cmd.Parameters.AddWithValue("@mcc", mcc);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqliteCommand("UPDATE boa_cards SET balance = (SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) FROM boa_ledger_entries WHERE account_id = @accId) WHERE card_id = @cardId", conn, tx))
            {
                cmd.Parameters.AddWithValue("@accId", accId);
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqliteCommand("UPDATE boa_authorizations SET status = 2, captured_date = @date WHERE authorization_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@id", authId);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>
    /// Authorized durumundaki bir provizyonu iptal eder (Void). Hiçbir muhasebe kaydı oluşmaz;
    /// yalnızca bloke edilen tutar (status değişikliğiyle) serbest bırakılır.
    /// </summary>
    private void ExecuteVoidAuthorization(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int authId = Convert.ToInt32(parameters["p_authorization_id"]);

            int status;
            using (var cmd = new SqliteCommand("SELECT status FROM boa_authorizations WHERE authorization_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", authId);
                var result = cmd.ExecuteScalar() ?? throw new Exception("Provizyon bulunamadi!");
                status = Convert.ToInt32(result);
            }

            if (status != 1)
            {
                throw new Exception("Bu provizyon Authorized durumda değil, iptal edilemez.");
            }

            using (var cmd = new SqliteCommand("UPDATE boa_authorizations SET status = 3, description = description || ' | VOID: ' || @reason WHERE authorization_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@reason", parameters["p_reason"]);
                cmd.Parameters.AddWithValue("@id", authId);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}

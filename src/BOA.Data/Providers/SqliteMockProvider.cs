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
                    card_brand INTEGER NOT NULL,         -- 1: Visa, 2: Mastercard, 3: Troy, 4: Amex
                    card_product INTEGER NOT NULL DEFAULT 1, -- 1: Classic, 2: Gold, 3: Platinum, 4: Business, 5: Premium
                    issuer_name TEXT NOT NULL DEFAULT 'BOA Bank'
                );";

            // 2. Kartlar Tablosu (Maskeli PAN, Şifreli PAN, PIN Block, GL Hesap referansı barındırır)
            string createCardsTable = @"
                CREATE TABLE IF NOT EXISTS boa_cards (
                    card_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_number TEXT NOT NULL,                  -- Maskeli Kart No (435520******1234)
                    encrypted_pan TEXT NOT NULL,                 -- Şifreli Kart No
                    pin_hash TEXT NULL,                         -- HSM PIN Block
                    card_holder_name TEXT NOT NULL,
                    emboss_name TEXT NOT NULL DEFAULT '',        -- EMBOSS formatında kart sahibi adı (max 21 karakter)
                    card_type INTEGER NOT NULL,
                    card_brand INTEGER NOT NULL DEFAULT 3,      -- 1: Visa, 2: Mastercard, 3: Troy, 4: Amex
                    card_product INTEGER NOT NULL DEFAULT 1,    -- 1: Classic, 2: Gold, 3: Platinum, 4: Business, 5: Premium
                    expiry_date TEXT NOT NULL,
                    status INTEGER NOT NULL DEFAULT 1,
                    card_limit REAL NOT NULL DEFAULT 0.00,
                    balance REAL NOT NULL DEFAULT 0.00,         -- Önbellek Bakiye
                    account_id INTEGER NOT NULL,                -- GL Hesabı
                    customer_id INTEGER NOT NULL,               -- Kart hamilinin müşteri kaydı
                    bank_account_id INTEGER NOT NULL,           -- Bağlı vadesiz/kredi hesabı
                    paycore_reference TEXT NULL,                -- Dış kart işleme sağlayıcısı (PayCore) tarafındaki karşılık gelen referans
                    cvv2_hash TEXT NULL,                        -- CVV2 hash (PCI-DSS: asla düz metin saklanmaz)
                    cvv_hash TEXT NULL,                         -- CVV (manyetik şerit) hash
                    service_code TEXT NOT NULL DEFAULT '201',   -- EMV Service Code
                    track2_data TEXT NULL,                      -- Track2 eşdeğer verisi
                    block_reason INTEGER NULL,                   -- BlockReason enum (sadece status=2 iken anlamlı)
                    blocked_date TEXT NULL,                      -- bloke tarihi
                    cancelled_date TEXT NULL,                    -- iptal tarihi
                    cancellation_reason INTEGER NULL,             -- CancellationReason enum
                    previous_card_id INTEGER NULL,               -- yenileme/reissue: önceki kart
                    replaced_by_card_id INTEGER NULL,            -- yenileme/reissue: bu kartı devralan yeni kart
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
                    paycore_auth_reference TEXT NULL,            -- PayCore tarafındaki karşılık gelen provizyon referansı
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

            // 5. PayCore "Outbox" Tablosu — ledger kaydıyla AYNI transaction'da (atomik) yazılır.
            // Süreç PayCore'u aramadan ÖNCE çökse bile, "PayCore'a bildirmem gerekiyordu" bilgisi
            // diskte kalır; bir mutabakat/retry işlemi bunu daha sonra tamamlayabilir (outbox pattern).
            string createPaycoreOutboxTable = @"
                CREATE TABLE IF NOT EXISTS boa_paycore_outbox (
                    outbox_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_id INTEGER NOT NULL,
                    amount REAL NOT NULL,
                    bank_reference_number TEXT UNIQUE NOT NULL,
                    status INTEGER NOT NULL DEFAULT 1,      -- 1: Pending, 2: Confirmed, 3: Declined, 4: FailedNeedsRetry
                    attempt_count INTEGER NOT NULL DEFAULT 0,
                    last_error TEXT NULL,
                    created_date TEXT NOT NULL,
                    updated_date TEXT NOT NULL,
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
            using (var cmd = new SqliteCommand(createPaycoreOutboxTable, connection)) { cmd.ExecuteNonQuery(); }

            // 5b. Kart Bloke Geçmişi Tablosu — kayıp/çalıntı bildirimi denetim izi.
            string createBlockHistoryTable = @"
                CREATE TABLE IF NOT EXISTS boa_card_block_history (
                    block_history_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_id INTEGER NOT NULL,
                    block_reason INTEGER NOT NULL,
                    is_emergency INTEGER NOT NULL DEFAULT 0,
                    description TEXT NOT NULL,
                    police_report_number TEXT NULL,
                    last_known_transaction_ref TEXT NULL,
                    replacement_requested INTEGER NOT NULL DEFAULT 0,
                    replacement_card_id INTEGER NULL,
                    user_id TEXT NOT NULL,
                    channel TEXT NOT NULL,
                    client_ip TEXT NOT NULL,
                    created_date TEXT NOT NULL,
                    FOREIGN KEY(card_id) REFERENCES boa_cards(card_id)
                );";
            using (var cmd = new SqliteCommand(createBlockHistoryTable, connection)) { cmd.ExecuteNonQuery(); }

            // 6. Limit Artış Talepleri Tablosu — Maker-Checker (çift onay) akışının veri katmanı.
            string createLimitChangeRequestsTable = @"
                CREATE TABLE IF NOT EXISTS boa_limit_change_requests (
                    limit_request_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_id INTEGER NOT NULL,
                    current_limit REAL NOT NULL,
                    requested_limit REAL NOT NULL,
                    status INTEGER NOT NULL DEFAULT 1,          -- 1: PendingApproval, 2: Approved, 3: Rejected
                    reason TEXT NOT NULL,
                    maker_user_id TEXT NOT NULL,
                    checker_user_id TEXT NULL,
                    decision_note TEXT NULL,
                    created_date TEXT NOT NULL,
                    decided_date TEXT NULL,
                    FOREIGN KEY(card_id) REFERENCES boa_cards(card_id)
                );";
            using (var cmd = new SqliteCommand(createLimitChangeRequestsTable, connection)) { cmd.ExecuteNonQuery(); }

            // 7. Kredi Kartı Başvuruları Tablosu — başvuru → skorlama → onay → basım akışı.
            string createCardApplicationsTable = @"
                CREATE TABLE IF NOT EXISTS boa_card_applications (
                    application_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    national_id TEXT NOT NULL,
                    applicant_name TEXT NOT NULL,
                    phone TEXT NULL,
                    declared_monthly_income REAL NOT NULL,
                    requested_limit REAL NOT NULL,
                    credit_score INTEGER NOT NULL,
                    bddk_limit_cap REAL NOT NULL,
                    approved_limit REAL NULL,
                    status INTEGER NOT NULL,
                    decision_reason TEXT NULL,
                    maker_user_id TEXT NOT NULL,
                    checker_user_id TEXT NULL,
                    card_id INTEGER NULL,
                    created_date TEXT NOT NULL,
                    decided_date TEXT NULL,
                    issued_date TEXT NULL,
                    FOREIGN KEY(card_id) REFERENCES boa_cards(card_id)
                );";
            using (var cmd = new SqliteCommand(createCardApplicationsTable, connection)) { cmd.ExecuteNonQuery(); }

            using (var cmd = new SqliteCommand(
                "INSERT OR IGNORE INTO boa_bin_table (bin_code, card_type, card_brand, card_product) VALUES " +
                "('435520', 1, 1, 1), " +     // Debit - Visa - Classic
                "('435521', 1, 1, 2), " +     // Debit - Visa - Gold
                "('543789', 2, 2, 1), " +     // Credit - Mastercard - Classic
                "('543790', 2, 2, 2), " +     // Credit - Mastercard - Gold
                "('979201', 2, 3, 1), " +     // Credit - Troy - Classic
                "('979202', 2, 3, 3), " +     // Credit - Troy - Platinum
                "('979203', 1, 3, 1);",       // Debit - Troy - Classic
                connection))
            {
                cmd.ExecuteNonQuery();
            }

            // 8. Fraud Kara Liste Tablosu — başvuru anında TCKN sorgulanır.
            string createFraudBlacklistTable = @"
                CREATE TABLE IF NOT EXISTS boa_fraud_blacklist (
                    blacklist_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    national_id TEXT UNIQUE NOT NULL,
                    reason TEXT NOT NULL,
                    created_date TEXT NOT NULL
                );";
            using (var cmd = new SqliteCommand(createFraudBlacklistTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(
                "INSERT OR IGNORE INTO boa_fraud_blacklist (national_id, reason, created_date) VALUES ('99999999999', 'Kara liste - sahte kimlik', datetime('now'))", connection))
            { cmd.ExecuteNonQuery(); }

            // 9. Kart Teslimat Takip Tablosu — basılan kartın kargo/şube yönetimi için.
            string createCardDeliveriesTable = @"
                CREATE TABLE IF NOT EXISTS boa_card_deliveries (
                    delivery_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    card_id INTEGER NOT NULL UNIQUE,
                    tracking_number TEXT NOT NULL,
                    delivered_date TEXT NULL,
                    FOREIGN KEY(card_id) REFERENCES boa_cards(card_id)
                );";
            using (var cmd = new SqliteCommand(createCardDeliveriesTable, connection)) { cmd.ExecuteNonQuery(); }

            // 10. Müşteri Bildirimleri Tablosu — SMS/e-posta denetim izi.
            string createNotificationsTable = @"
                CREATE TABLE IF NOT EXISTS boa_notifications (
                    notification_id INTEGER PRIMARY KEY AUTOINCREMENT,
                    national_id TEXT NOT NULL,
                    channel TEXT NOT NULL,
                    template_key TEXT NOT NULL,
                    message TEXT NOT NULL,
                    created_date TEXT NOT NULL
                );";
            using (var cmd = new SqliteCommand(createNotificationsTable, connection)) { cmd.ExecuteNonQuery(); }

            // 11. Limit Yönetimi — yeni tablolar
            string createTempLimitsTable = @"CREATE TABLE IF NOT EXISTS boa_temporary_limits (temp_limit_id INTEGER PRIMARY KEY AUTOINCREMENT, card_id INTEGER NOT NULL, original_limit REAL NOT NULL, temporary_limit REAL NOT NULL, start_date TEXT NOT NULL, expiry_date TEXT NOT NULL, is_active INTEGER NOT NULL DEFAULT 1, reason TEXT NOT NULL, created_by_user_id TEXT NOT NULL, created_date TEXT NOT NULL, reverted_date TEXT NULL, FOREIGN KEY(card_id) REFERENCES boa_cards(card_id));";
            string createSpendingLimitsTable = @"CREATE TABLE IF NOT EXISTS boa_spending_limits (spending_limit_id INTEGER PRIMARY KEY AUTOINCREMENT, card_id INTEGER NOT NULL, limit_type INTEGER NOT NULL, limit_amount REAL NOT NULL, used_today REAL NOT NULL DEFAULT 0, used_this_month REAL NOT NULL DEFAULT 0, last_reset_date TEXT NOT NULL, FOREIGN KEY(card_id) REFERENCES boa_cards(card_id));";
            string createInstallmentPlansTable = @"CREATE TABLE IF NOT EXISTS boa_installment_plans (plan_id INTEGER PRIMARY KEY AUTOINCREMENT, card_id INTEGER NOT NULL, total_amount REAL NOT NULL, installment_count INTEGER NOT NULL, installment_amount REAL NOT NULL, remaining_installments INTEGER NOT NULL, mcc TEXT NULL, merchant_id TEXT NULL, reference_number TEXT NOT NULL, created_date TEXT NOT NULL, FOREIGN KEY(card_id) REFERENCES boa_cards(card_id));";
            string createMccRulesTable = @"CREATE TABLE IF NOT EXISTS boa_mcc_installment_rules (mcc_code TEXT PRIMARY KEY, mcc_description TEXT NOT NULL, max_installment_count INTEGER NOT NULL);";
            using (var cmd = new SqliteCommand(createTempLimitsTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createSpendingLimitsTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createInstallmentPlansTable, connection)) { cmd.ExecuteNonQuery(); }
            using (var cmd = new SqliteCommand(createMccRulesTable, connection)) { cmd.ExecuteNonQuery(); }
            // MCC seed verileri
            using (var cmd = new SqliteCommand(@"INSERT OR IGNORE INTO boa_mcc_installment_rules VALUES ('5411','Market',0),('5812','Restoran',0),('5944','Kuyumcu',4),('5732','Elektronik',12),('5712','Mobilya',24),('5311','Magaza',12),('6011','ATM Nakit',0),('0000','Varsayilan',12)", connection)) { cmd.ExecuteNonQuery(); }

            // Geliştiricinin diskinde bu değişiklikten ÖNCE oluşturulmuş bir boa_mock.db dosyası olabilir;
            // CREATE TABLE IF NOT EXISTS böyle bir durumda yeni kolonları eklemez. Var olan bir dosyada da
            // çalışabilmesi için eksik kolonları burada tamamlıyoruz (SQLite'ta "ADD COLUMN IF NOT EXISTS" yok).
            TryAddColumn(connection, "boa_cards", "paycore_reference", "TEXT NULL");
            TryAddColumn(connection, "boa_authorizations", "paycore_auth_reference", "TEXT NULL");
            TryAddColumn(connection, "boa_cards", "emboss_name", "TEXT NOT NULL DEFAULT ''");
            TryAddColumn(connection, "boa_cards", "card_brand", "INTEGER NOT NULL DEFAULT 3");
            TryAddColumn(connection, "boa_cards", "card_product", "INTEGER NOT NULL DEFAULT 1");
            TryAddColumn(connection, "boa_cards", "cvv2_hash", "TEXT NULL");
            TryAddColumn(connection, "boa_cards", "cvv_hash", "TEXT NULL");
            TryAddColumn(connection, "boa_cards", "service_code", "TEXT NOT NULL DEFAULT '201'");
            TryAddColumn(connection, "boa_cards", "track2_data", "TEXT NULL");
            TryAddColumn(connection, "boa_cards", "block_reason", "INTEGER NULL");
            TryAddColumn(connection, "boa_cards", "blocked_date", "TEXT NULL");
            TryAddColumn(connection, "boa_cards", "cancelled_date", "TEXT NULL");
            TryAddColumn(connection, "boa_cards", "cancellation_reason", "INTEGER NULL");
            TryAddColumn(connection, "boa_cards", "previous_card_id", "INTEGER NULL");
            TryAddColumn(connection, "boa_cards", "replaced_by_card_id", "INTEGER NULL");
            TryAddColumn(connection, "boa_cards", "cash_advance_limit", "REAL NOT NULL DEFAULT 0.00");
            TryAddColumn(connection, "boa_cards", "installment_limit", "REAL NOT NULL DEFAULT 0.00");
            TryAddColumn(connection, "boa_cards", "daily_atm_limit", "REAL NOT NULL DEFAULT 5000.00");
            TryAddColumn(connection, "boa_cards", "daily_pos_limit", "REAL NOT NULL DEFAULT 25000.00");
            TryAddColumn(connection, "boa_cards", "monthly_spending_limit", "REAL NOT NULL DEFAULT 0.00");
        }
    }

    private static void TryAddColumn(SqliteConnection connection, string table, string column, string definition)
    {
        try
        {
            using var cmd = new SqliteCommand($"ALTER TABLE {table} ADD COLUMN {column} {definition}", connection);
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // Kolon zaten mevcutsa SQLite "duplicate column name" hatası fırlatır — yoksayılır.
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
                    if (parameters.TryGetValue("p_national_id", out var nationalIdFilter) && nationalIdFilter != null && !string.IsNullOrEmpty(nationalIdFilter.ToString()))
                    {
                        query += " AND cu.national_id = @nid";
                        listCmd.Parameters.AddWithValue("@nid", nationalIdFilter);
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

                case "sp_boa_card_report_lost_stolen":
                    ExecuteReportLostStolen(connection, parameters);
                    var selectBlockedCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                    selectBlockedCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectBlockedCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_void_active_auths":
                    {
                        int voidedCount = ExecuteVoidActiveAuths(connection, parameters);
                        dt.Columns.Add("voided_count", typeof(int));
                        var vrow = dt.NewRow();
                        vrow["voided_count"] = voidedCount;
                        dt.Rows.Add(vrow);
                    }
                    break;

                case "sp_boa_card_cancel":
                    ExecuteCancelCard(connection, parameters);
                    var selectCancelledCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                    selectCancelledCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectCancelledCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_create_replacement":
                    ExecuteCreateReplacement(connection, parameters);
                    var selectReplCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                    selectReplCmd.Parameters.AddWithValue("@cardId", parameters["p_new_card_id"]);
                    using (var reader = selectReplCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_card_set_replacement_link":
                    {
                        using var cmd = new SqliteCommand(
                            "UPDATE boa_cards SET replaced_by_card_id = @newId WHERE card_id = @oldId", connection);
                        cmd.Parameters.AddWithValue("@newId", parameters["p_new_card_id"]);
                        cmd.Parameters.AddWithValue("@oldId", parameters["p_previous_card_id"]);
                        cmd.ExecuteNonQuery();
                        dt.Columns.Add("result", typeof(string));
                        var row = dt.NewRow();
                        row["result"] = "OK";
                        dt.Rows.Add(row);
                    }
                    break;

                case "sp_boa_card_create_transaction":
                    // >>> ADIM 11: switch dispatcher buraya düşüyor (spName = "sp_boa_card_create_transaction").
                    // >>> ADIM 12: ExecuteCreateTransaction'a giriliyor — asıl kilit/limit/ledger mantığı orada.
                    // 5. Kart Hareketi Ekleme Simülasyonu (Çift Kayıt Muhasebe ile)
                    int newEntryId = ExecuteCreateTransaction(connection, parameters);

                    // >>> ADIM 13: ExecuteCreateTransaction'dan dönüldü — yeni yevmiye satırı (entryId) tekrar
                    // okunup DataTable'a dolduruluyor (bu DataTable, ADIM 9'daki DbManager.ExecuteReader'a döner).
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
                    var selectSecureCmd = new SqliteCommand(
                        "SELECT c.card_id, c.card_number, c.encrypted_pan, c.pin_hash, c.paycore_reference, c.status, cu.national_id AS national_id " +
                        "FROM boa_cards c JOIN boa_customers cu ON c.customer_id = cu.customer_id WHERE c.card_id = @cardId", connection);
                    selectSecureCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectSecureCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_bin_lookup":
                    // 8. Kart türüne göre BIN kodu sorgulama (BKM BIN tablosu simülasyonu)
                    var binCmd = new SqliteCommand(
                        "SELECT bin_code, card_brand, card_product FROM boa_bin_table WHERE card_type = @cardType ORDER BY card_product LIMIT 1", connection);
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

                case "sp_boa_card_set_paycore_reference":
                    // PayCore entegrasyonu: kart PayCore'da (issuing) kaydedildikten sonra dönen referansı
                    // bankanın kendi kart kaydına işler. Bu ikisi arasındaki TEK bağ budur.
                    using (var cmd = new SqliteCommand("UPDATE boa_cards SET paycore_reference = @ref WHERE card_id = @cardId", connection))
                    {
                        cmd.Parameters.AddWithValue("@ref", parameters["p_paycore_reference"]);
                        cmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                        cmd.ExecuteNonQuery();
                    }
                    var selectPaycoreCardCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                    selectPaycoreCardCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectPaycoreCardCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_auth_set_paycore_reference":
                    // Bankanın kendi kararı onayladıktan SONRA, PayCore'un provizyonu da onaylamasıyla dönen referans.
                    using (var cmd = new SqliteCommand("UPDATE boa_authorizations SET paycore_auth_reference = @ref WHERE authorization_id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@ref", parameters["p_paycore_auth_reference"]);
                        cmd.Parameters.AddWithValue("@id", parameters["p_authorization_id"]);
                        cmd.ExecuteNonQuery();
                    }
                    var selectPaycoreAuthCmd = new SqliteCommand("SELECT * FROM boa_authorizations WHERE authorization_id = @id", connection);
                    selectPaycoreAuthCmd.Parameters.AddWithValue("@id", parameters["p_authorization_id"]);
                    using (var reader = selectPaycoreAuthCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_auth_override_decline":
                    // Bankanın kendi limit kontrolü onaylayıp bir hold oluşturduktan SONRA, PayCore'un
                    // provizyonu reddetmesi durumunda bu holdün geri alınmasını (declined'a çevrilmesini)
                    // sağlar. Yalnızca hâlâ "Authorized" (status=1) durumdaki bir kayıt üzerinde çalışır —
                    // zaten capture/void edilmiş bir provizyona dokunmaz.
                    using (var cmd = new SqliteCommand(@"
                        UPDATE boa_authorizations
                        SET status = 4, response_code = @responseCode, authorization_code = NULL,
                            description = description || ' | PAYCORE_DECLINED: ' || @reason
                        WHERE authorization_id = @id AND status = 1", connection))
                    {
                        cmd.Parameters.AddWithValue("@responseCode", parameters["p_response_code"]);
                        cmd.Parameters.AddWithValue("@reason", parameters["p_reason"]);
                        cmd.Parameters.AddWithValue("@id", parameters["p_authorization_id"]);
                        cmd.ExecuteNonQuery();
                    }
                    var selectOverrideCmd = new SqliteCommand("SELECT * FROM boa_authorizations WHERE authorization_id = @id", connection);
                    selectOverrideCmd.Parameters.AddWithValue("@id", parameters["p_authorization_id"]);
                    using (var reader = selectOverrideCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_paycore_outbox_mark":
                    // PayCore çağrısının sonucunu (onaylandı/reddedildi/ulaşılamadı) outbox satırına işler.
                    // FailedNeedsRetry'e her işaretlemede attempt_count bir artırılır (yeniden deneme sayacı).
                    using (var cmd = new SqliteCommand(@"
                        UPDATE boa_paycore_outbox
                        SET status = @status,
                            last_error = @lastError,
                            attempt_count = attempt_count + CASE WHEN @status = 4 THEN 1 ELSE 0 END,
                            updated_date = @date
                        WHERE bank_reference_number = @refNo", connection))
                    {
                        cmd.Parameters.AddWithValue("@status", parameters["p_status"]);
                        cmd.Parameters.AddWithValue("@lastError", parameters.TryGetValue("p_last_error", out var lastErr) && lastErr != null ? lastErr : DBNull.Value);
                        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("@refNo", parameters["p_bank_reference_number"]);
                        cmd.ExecuteNonQuery();
                    }
                    var selectOutboxMarkCmd = new SqliteCommand("SELECT * FROM boa_paycore_outbox WHERE bank_reference_number = @refNo", connection);
                    selectOutboxMarkCmd.Parameters.AddWithValue("@refNo", parameters["p_bank_reference_number"]);
                    using (var reader = selectOutboxMarkCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_paycore_outbox_get_pending":
                    // Mutabakat/yeniden deneme işleminin taradığı kuyruk: PayCore'a hiç ulaşılamamış (Pending,
                    // henüz çağrılmadıysa — pratikte bu satır ana akışta anında güncellenir) veya ulaşılıp da
                    // ağ hatası alınmış (FailedNeedsRetry) satırlar. Karta ait paycore_reference de JOIN'lenir.
                    var outboxPendingCmd = new SqliteCommand(@"
                        SELECT o.*, c.paycore_reference AS card_paycore_reference
                        FROM boa_paycore_outbox o
                        JOIN boa_cards c ON o.card_id = c.card_id
                        WHERE o.status IN (1, 4)
                        ORDER BY o.created_date", connection);
                    using (var reader = outboxPendingCmd.ExecuteReader())
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
                        cmd.Parameters.AddWithValue("@debt", Convert.ToDecimal(parameters["p_total_debt"]));
                        cmd.Parameters.AddWithValue("@minPay", Convert.ToDecimal(parameters["p_minimum_payment"]));
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

                case "sp_boa_limit_request_create":
                    // Maker-checker: yeni bir limit artış talebi oluşturur (PendingApproval).
                    int newReqId;
                    using (var cmd = new SqliteCommand(@"
                        INSERT INTO boa_limit_change_requests (card_id, current_limit, requested_limit, status, reason, maker_user_id, created_date)
                        VALUES (@cardId, @curLimit, @reqLimit, 1, @reason, @maker, @date);
                        SELECT last_insert_rowid();", connection))
                    {
                        cmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                        cmd.Parameters.AddWithValue("@curLimit", Convert.ToDecimal(parameters["p_current_limit"]));
                        cmd.Parameters.AddWithValue("@reqLimit", Convert.ToDecimal(parameters["p_requested_limit"]));
                        cmd.Parameters.AddWithValue("@reason", parameters["p_reason"]);
                        cmd.Parameters.AddWithValue("@maker", parameters["p_maker_user_id"]);
                        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        newReqId = Convert.ToInt32(cmd.ExecuteScalar());
                    }
                    using (var cmd = new SqliteCommand("SELECT * FROM boa_limit_change_requests WHERE limit_request_id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", newReqId);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_limit_request_decide":
                    // Maker-checker: onay/red kararı. Kart limitini BURADA güncellemez — bu SP yalnızca
                    // talep kaydını günceller; limit uygulaması CardService tarafında (SP dışında) yapılır.
                    ExecuteDecideLimitRequest(connection, parameters);
                    using (var cmd = new SqliteCommand("SELECT * FROM boa_limit_change_requests WHERE limit_request_id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", parameters["p_limit_request_id"]);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_limit_request_list":
                    // Limit artış taleplerini listeler.
                    {
                        string reqQuery = "SELECT * FROM boa_limit_change_requests WHERE 1=1";
                        var reqCmd = new SqliteCommand();
                        reqCmd.Connection = connection;

                        if (parameters.TryGetValue("p_card_id", out var reqCardId) && reqCardId != null)
                        {
                            reqQuery += " AND card_id = @cardId";
                            reqCmd.Parameters.AddWithValue("@cardId", reqCardId);
                        }
                        if (parameters.TryGetValue("p_only_pending", out var onlyPending) && Convert.ToBoolean(onlyPending))
                        {
                            reqQuery += " AND status = 1";
                        }
                        reqQuery += " ORDER BY limit_request_id DESC";
                        reqCmd.CommandText = reqQuery;

                        using var reader = reqCmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_limit_request_get":
                    // Tek bir limit artış talebini ID ile getirir.
                    using (var cmd = new SqliteCommand("SELECT * FROM boa_limit_change_requests WHERE limit_request_id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", parameters["p_limit_request_id"]);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_application_create":
                    {
                        int newAppId;
                        using (var cmd = new SqliteCommand(@"
                            INSERT INTO boa_card_applications (
                                national_id, applicant_name, phone, declared_monthly_income, requested_limit,
                                credit_score, bddk_limit_cap, approved_limit, status, decision_reason,
                                maker_user_id, created_date)
                            VALUES (@nid, @name, @phone, @income, @reqLimit, @score, @cap, @approved, @status, @reason, @maker, @date);
                            SELECT last_insert_rowid();", connection))
                        {
                            cmd.Parameters.AddWithValue("@nid", parameters["p_national_id"]);
                            cmd.Parameters.AddWithValue("@name", parameters["p_applicant_name"]);
                            cmd.Parameters.AddWithValue("@phone", parameters.TryGetValue("p_phone", out var ph) && ph != null && !string.IsNullOrWhiteSpace(ph.ToString()) ? ph : DBNull.Value);
                            cmd.Parameters.AddWithValue("@income", Convert.ToDecimal(parameters["p_declared_monthly_income"]));
                            cmd.Parameters.AddWithValue("@reqLimit", Convert.ToDecimal(parameters["p_requested_limit"]));
                            cmd.Parameters.AddWithValue("@score", Convert.ToInt32(parameters["p_credit_score"]));
                            cmd.Parameters.AddWithValue("@cap", Convert.ToDecimal(parameters["p_bddk_limit_cap"]));
                            cmd.Parameters.AddWithValue("@approved", parameters.TryGetValue("p_approved_limit", out var al) && al != null && al != DBNull.Value ? al : DBNull.Value);
                            cmd.Parameters.AddWithValue("@status", Convert.ToInt32(parameters["p_status"]));
                            cmd.Parameters.AddWithValue("@reason", parameters.TryGetValue("p_decision_reason", out var dr) && dr != null ? dr : DBNull.Value);
                            cmd.Parameters.AddWithValue("@maker", parameters["p_maker_user_id"]);
                            cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            newAppId = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        using (var cmd = new SqliteCommand("SELECT * FROM boa_card_applications WHERE application_id = @id", connection))
                        {
                            cmd.Parameters.AddWithValue("@id", newAppId);
                            using var reader = cmd.ExecuteReader();
                            LoadReaderIntoTable(dt, reader);
                        }
                    }
                    break;

                case "sp_boa_application_get":
                    using (var cmd = new SqliteCommand("SELECT * FROM boa_card_applications WHERE application_id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", parameters["p_application_id"]);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_application_list":
                    {
                        string appQuery = "SELECT * FROM boa_card_applications WHERE 1=1";
                        var appCmd = new SqliteCommand();
                        appCmd.Connection = connection;

                        if (parameters.TryGetValue("p_status", out var appStatus) && appStatus != null)
                        {
                            appQuery += " AND status = @status";
                            appCmd.Parameters.AddWithValue("@status", appStatus);
                        }
                        if (parameters.TryGetValue("p_national_id", out var appNid) && appNid != null && !string.IsNullOrEmpty(appNid.ToString()))
                        {
                            appQuery += " AND national_id = @nid";
                            appCmd.Parameters.AddWithValue("@nid", appNid);
                        }
                        if (parameters.TryGetValue("p_only_open", out var onlyOpen) && Convert.ToBoolean(onlyOpen))
                        {
                            appQuery += " AND status IN (1, 2, 3)";
                        }
                        if (parameters.TryGetValue("p_only_issuable", out var onlyIssuable) && Convert.ToBoolean(onlyIssuable))
                        {
                            appQuery += " AND status IN (2, 3)";
                        }
                        appQuery += " ORDER BY application_id DESC";
                        appCmd.CommandText = appQuery;

                        using var reader = appCmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_application_decide":
                    ExecuteDecideApplication(connection, parameters);
                    using (var cmd = new SqliteCommand("SELECT * FROM boa_card_applications WHERE application_id = @id", connection))
                    {
                        cmd.Parameters.AddWithValue("@id", parameters["p_application_id"]);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_application_mark_issued":
                    {
                        using (var cmd = new SqliteCommand(@"
                            UPDATE boa_card_applications
                            SET status = 6, card_id = @cardId, issued_date = @issued
                            WHERE application_id = @id AND status IN (2, 3)", connection))
                        {
                            cmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                            cmd.Parameters.AddWithValue("@issued", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            cmd.Parameters.AddWithValue("@id", parameters["p_application_id"]);
                            cmd.ExecuteNonQuery();
                        }
                        using (var cmd = new SqliteCommand("SELECT * FROM boa_card_applications WHERE application_id = @id", connection))
                        {
                            cmd.Parameters.AddWithValue("@id", parameters["p_application_id"]);
                            using var reader = cmd.ExecuteReader();
                            LoadReaderIntoTable(dt, reader);
                        }
                    }
                    break;

                case "sp_boa_application_expire":
                    {
                        string cutoff = Convert.ToDateTime(parameters["p_cutoff"]).ToString("yyyy-MM-dd HH:mm:ss");
                        using (var cmd = new SqliteCommand(@"
                            UPDATE boa_card_applications
                            SET status = 7, decided_date = @now, decision_reason = 'Zaman aşımı (EOD)'
                            WHERE status = 1 AND created_date < @cutoff", connection))
                        {
                            cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                            cmd.Parameters.AddWithValue("@cutoff", cutoff);
                            cmd.ExecuteNonQuery();
                        }
                        using (var countCmd = new SqliteCommand("SELECT changes()", connection))
                        {
                            dt.Columns.Add("expired_count", typeof(int));
                            var countRow = dt.NewRow();
                            countRow["expired_count"] = Convert.ToInt32(countCmd.ExecuteScalar());
                            dt.Rows.Add(countRow);
                        }
                    }
                    break;

                case "sp_boa_card_activate":
                    ExecuteActivateCard(connection, parameters);
                    var selectActivatedCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                    selectActivatedCmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                    using (var reader = selectActivatedCmd.ExecuteReader())
                    {
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_fraud_check":
                    {
                        string nid = parameters["p_national_id"].ToString() ?? string.Empty;
                        using var cmd = new SqliteCommand("SELECT * FROM boa_fraud_blacklist WHERE national_id = @nid", connection);
                        cmd.Parameters.AddWithValue("@nid", nid);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_rapid_application_check":
                    {
                        string nid = parameters["p_national_id"].ToString() ?? string.Empty;
                        using var cmd = new SqliteCommand(
                            "SELECT COUNT(*) AS cnt FROM boa_card_applications WHERE national_id = @nid AND created_date > @window", connection);
                        cmd.Parameters.AddWithValue("@nid", nid);
                        cmd.Parameters.AddWithValue("@window", DateTime.Now.AddHours(-24).ToString("yyyy-MM-dd HH:mm:ss"));
                        dt.Columns.Add("cnt", typeof(int));
                        var row = dt.NewRow();
                        row["cnt"] = Convert.ToInt32(cmd.ExecuteScalar());
                        dt.Rows.Add(row);
                    }
                    break;

                case "sp_boa_notification_create":
                    {
                        string nationalId = parameters["p_national_id"].ToString() ?? string.Empty;
                        string channel = parameters["p_channel"].ToString() ?? "SMS";
                        string templateKey = parameters["p_template_key"].ToString() ?? string.Empty;
                        string message = parameters["p_message"].ToString() ?? string.Empty;
                        using var cmd = new SqliteCommand(@"
                            INSERT INTO boa_notifications (national_id, channel, template_key, message, created_date)
                            VALUES (@nid, @ch, @tk, @msg, @date); SELECT last_insert_rowid();", connection);
                        cmd.Parameters.AddWithValue("@nid", nationalId);
                        cmd.Parameters.AddWithValue("@ch", channel);
                        cmd.Parameters.AddWithValue("@tk", templateKey);
                        cmd.Parameters.AddWithValue("@msg", message);
                        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        dt.Columns.Add("notification_id", typeof(int));
                        var row = dt.NewRow();
                        row["notification_id"] = Convert.ToInt32(cmd.ExecuteScalar());
                        dt.Rows.Add(row);
                    }
                    break;

                case "sp_boa_card_deliver":
                    {
                        int cardId = Convert.ToInt32(parameters["p_card_id"]);
                        string tracking = parameters.TryGetValue("p_tracking_number", out var trk) && trk != null
                            ? trk.ToString() ?? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()
                            : Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                        using var tx = connection.BeginTransaction();
                        try
                        {
                            using (var upd = new SqliteCommand(
                                "UPDATE boa_cards SET status = 4 WHERE card_id = @id AND status = 5", connection, tx))
                            {
                                upd.Parameters.AddWithValue("@id", cardId);
                                if (upd.ExecuteNonQuery() == 0)
                                    throw new Exception("Kart InTransit durumunda değil veya bulunamadı.");
                            }
                            using (var ins = new SqliteCommand(@"
                                INSERT OR REPLACE INTO boa_card_deliveries (card_id, tracking_number, delivered_date)
                                VALUES (@id, @trk, @date)", connection, tx))
                            {
                                ins.Parameters.AddWithValue("@id", cardId);
                                ins.Parameters.AddWithValue("@trk", tracking);
                                ins.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                                ins.ExecuteNonQuery();
                            }
                            tx.Commit();
                        }
                        catch { tx.Rollback(); throw; }
                        var selectDeliveredCmd = new SqliteCommand(CardWithCustomerSelect + " WHERE c.card_id = @cardId", connection);
                        selectDeliveredCmd.Parameters.AddWithValue("@cardId", cardId);
                        using (var reader = selectDeliveredCmd.ExecuteReader())
                        {
                            LoadReaderIntoTable(dt, reader);
                        }
                    }
                    break;

                case "sp_boa_spending_limit_get":
                    {
                        var cmd = new SqliteCommand("SELECT * FROM boa_spending_limits WHERE card_id = @cardId", connection);
                        cmd.Parameters.AddWithValue("@cardId", parameters["p_card_id"]);
                        using var reader = cmd.ExecuteReader();
                        LoadReaderIntoTable(dt, reader);
                    }
                    break;

                case "sp_boa_spending_limit_upsert":
                    {
                        int cardId = Convert.ToInt32(parameters["p_card_id"]);
                        int limitType = Convert.ToInt32(parameters["p_limit_type"]);
                        decimal amount = Convert.ToDecimal(parameters["p_limit_amount"]);
                        string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        using var cmd = new SqliteCommand(@"INSERT INTO boa_spending_limits (card_id, limit_type, limit_amount, used_today, used_this_month, last_reset_date)
                            VALUES (@cardId, @type, @amount, 0, 0, @date)
                            ON CONFLICT(card_id) DO UPDATE SET limit_amount=@amount, last_reset_date=@date WHERE limit_type=@type", connection);
                        cmd.Parameters.AddWithValue("@cardId", cardId);
                        cmd.Parameters.AddWithValue("@type", limitType);
                        cmd.Parameters.AddWithValue("@amount", amount);
                        cmd.Parameters.AddWithValue("@date", now);
                        cmd.ExecuteNonQuery();
                        dt.Columns.Add("result", typeof(string));
                        dt.Rows.Add(dt.NewRow());
                        dt.Rows[0]["result"] = "OK";
                    }
                    break;

                case "sp_boa_regulatory_report":
                    {
                        string reportType = parameters["p_report_type"].ToString() ?? "daily_summary";
                        string reportDate = parameters.TryGetValue("p_report_date", out var rd) && rd != null
                            ? Convert.ToDateTime(rd).ToString("yyyy-MM-dd")
                            : DateTime.Now.ToString("yyyy-MM-dd");
                        dt.Columns.Add("report_type", typeof(string));
                        dt.Columns.Add("report_date", typeof(string));
                        dt.Columns.Add("report_data", typeof(string));
                        var row = dt.NewRow();
                        row["report_type"] = reportType;
                        row["report_date"] = reportDate;

                        if (reportType == "daily_summary")
                        {
                            int applied, approved, rejected, issued;
                            using (var c = new SqliteCommand(
                                "SELECT COUNT(*) FROM boa_card_applications WHERE date(created_date) = @d", connection))
                            { c.Parameters.AddWithValue("@d", reportDate); applied = Convert.ToInt32(c.ExecuteScalar()); }
                            using (var c = new SqliteCommand(
                                "SELECT COUNT(*) FROM boa_card_applications WHERE date(created_date) = @d AND status IN (2,3)", connection))
                            { c.Parameters.AddWithValue("@d", reportDate); approved = Convert.ToInt32(c.ExecuteScalar()); }
                            using (var c = new SqliteCommand(
                                "SELECT COUNT(*) FROM boa_card_applications WHERE date(created_date) = @d AND status IN (4,5)", connection))
                            { c.Parameters.AddWithValue("@d", reportDate); rejected = Convert.ToInt32(c.ExecuteScalar()); }
                            using (var c = new SqliteCommand(
                                "SELECT COUNT(*) FROM boa_card_applications WHERE date(issued_date) = @d AND status = 6", connection))
                            { c.Parameters.AddWithValue("@d", reportDate); issued = Convert.ToInt32(c.ExecuteScalar()); }

                            decimal totalLimit = 0; int totalCards = 0, overdueCards = 0;
                            using (var c = new SqliteCommand(
                                "SELECT COUNT(*), COALESCE(SUM(card_limit),0) FROM boa_cards WHERE status IN (1,2)", connection))
                            { using var r = c.ExecuteReader(); if (r.Read()) { totalCards = r.GetInt32(0); totalLimit = r.GetDecimal(1); } }
                            using (var c = new SqliteCommand(
                                "SELECT COUNT(DISTINCT s.card_id) FROM boa_statements s WHERE s.is_paid = 0 AND s.due_date < @now", connection))
                            { c.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); overdueCards = Convert.ToInt32(c.ExecuteScalar()); }

                            row["report_data"] = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                applications = new { total = applied, approved, rejected },
                                cards_issued = issued,
                                portfolio = new
                                {
                                    total_cards = totalCards,
                                    total_limit = totalLimit,
                                    overdue_cards = overdueCards,
                                    overdue_ratio = totalCards > 0 ? $"{overdueCards * 100.0 / totalCards:F1}%" : "0%"
                                }
                            });
                        }
                        else
                        {
                            row["report_data"] = System.Text.Json.JsonSerializer.Serialize(new
                            {
                                report = reportType,
                                message = "Desteklenen rapor tipi: daily_summary"
                            });
                        }
                        dt.Rows.Add(row);
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
            int cardStatus = parameters.TryGetValue("p_status", out var statusParam) && statusParam != null
                ? Convert.ToInt32(statusParam)
                : 1;
            string insertSql = @"
                INSERT INTO boa_cards (card_number, encrypted_pan, pin_hash, card_holder_name, emboss_name, card_type, card_brand, card_product, expiry_date, status, card_limit, balance, account_id, customer_id, bank_account_id, cvv2_hash, cvv_hash, service_code, track2_data, created_date)
                VALUES (@num, @enc, @pin, @name, @emboss, @type, @brand, @product, @expiry, @status, @limit, @balance, @accId, @custId, @bankAccId, @cvv2h, @cvvh, @svc, @track2, @created); SELECT last_insert_rowid();";

            int cardId = 0;
            using (var cmd = new SqliteCommand(insertSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@num", parameters["p_card_number"]);
                cmd.Parameters.AddWithValue("@enc", parameters["p_encrypted_pan"]);
                cmd.Parameters.AddWithValue("@pin", parameters["p_pin_hash"] ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@name", parameters["p_card_holder_name"]);
                cmd.Parameters.AddWithValue("@emboss", parameters.TryGetValue("p_emboss_name", out var emb) ? emb?.ToString() ?? "" : "");
                cmd.Parameters.AddWithValue("@type", parameters["p_card_type"]);
                cmd.Parameters.AddWithValue("@brand", parameters.TryGetValue("p_card_brand", out var br) ? Convert.ToInt32(br) : 3);
                cmd.Parameters.AddWithValue("@product", parameters.TryGetValue("p_card_product", out var pr) ? Convert.ToInt32(pr) : 1);
                cmd.Parameters.AddWithValue("@expiry", Convert.ToDateTime(parameters["p_expiry_date"]).ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@status", cardStatus);
                cmd.Parameters.AddWithValue("@limit", Convert.ToDecimal(parameters["p_limit"]));
                cmd.Parameters.AddWithValue("@balance", Convert.ToDecimal(parameters["p_initial_balance"]));
                cmd.Parameters.AddWithValue("@accId", accId);
                cmd.Parameters.AddWithValue("@custId", customerId);
                cmd.Parameters.AddWithValue("@bankAccId", bankAccountId);
                cmd.Parameters.AddWithValue("@cvv2h", parameters.TryGetValue("p_cvv2_hash", out var cvv2h) ? cvv2h?.ToString() : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@cvvh", parameters.TryGetValue("p_cvv_hash", out var cvvh) ? cvvh?.ToString() : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@svc", parameters.TryGetValue("p_service_code", out var svc) ? svc?.ToString() ?? "201" : "201");
                cmd.Parameters.AddWithValue("@track2", parameters.TryGetValue("p_track2_data", out var trk2) ? trk2?.ToString() : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@created", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cardId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            // C. İlk Bakiye Tanımlaması Varsa Çift Kayıt Yevmiye Girişi Yap (Credit)
            decimal initialBalance = Convert.ToDecimal(parameters["p_initial_balance"]);
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

    private void ExecuteDecideApplication(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int appId = Convert.ToInt32(parameters["p_application_id"]);
            bool approve = Convert.ToBoolean(parameters["p_approve"]);
            string checkerUserId = parameters["p_checker_user_id"].ToString() ?? string.Empty;
            string decisionNote = parameters.TryGetValue("p_decision_note", out var dn) && dn != null ? dn.ToString()! : string.Empty;
            int newStatus = approve ? 3 : 5; // Approved or Rejected

            using (var cmd = new SqliteCommand(@"
                UPDATE boa_card_applications
                SET status = @status, approved_limit = @approvedLimit, checker_user_id = @checker,
                    decision_reason = @note, decided_date = @date
                WHERE application_id = @id AND status = 1", conn, tx))
            {
                cmd.Parameters.AddWithValue("@status", newStatus);
                cmd.Parameters.AddWithValue("@approvedLimit", approve && parameters.TryGetValue("p_approved_limit", out var al) && al != null && al != DBNull.Value ? al : DBNull.Value);
                cmd.Parameters.AddWithValue("@checker", checkerUserId);
                cmd.Parameters.AddWithValue("@note", decisionNote);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@id", appId);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                    throw new Exception("Başvuru ManualReview durumunda değil veya bulunamadı.");
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private void ExecuteActivateCard(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            string pinHash = parameters["p_pin_hash"].ToString() ?? string.Empty;

            using (var cmd = new SqliteCommand(
                "UPDATE boa_cards SET status = 1, pin_hash = @pin WHERE card_id = @id AND status = 4", conn, tx))
            {
                cmd.Parameters.AddWithValue("@pin", pinHash);
                cmd.Parameters.AddWithValue("@id", cardId);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                    throw new Exception("Kart PendingActivation durumunda değil veya bulunamadı.");
            }

            using (var cmd = new SqliteCommand(@"
                INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip, log_date)
                VALUES (@cardId, 'CARD_ACTIVATED', '4', '1', 'Kart aktivasyonu (TCKN + PIN)', @user, @channel, @ip, @date);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
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
            decimal newLimit = Convert.ToDecimal(parameters["p_new_limit"]);

            // SQLite'ta FOR UPDATE yoktur; gerçek ortamlarda (Oracle/Postgres) sp_boa_card_update_limit
            // SELECT ... FOR UPDATE ile satır kilidini alır. Burada yalnızca eski limiti okuyoruz.
            decimal oldLimit = 0m;
            using (var cmd = new SqliteCommand("SELECT card_limit FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                oldLimit = Convert.ToDecimal(cmd.ExecuteScalar());
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

    private void ExecuteDecideLimitRequest(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int reqId = Convert.ToInt32(parameters["p_limit_request_id"]);
            bool approve = Convert.ToBoolean(parameters["p_approve"]);
            string checkerUserId = parameters["p_checker_user_id"].ToString() ?? string.Empty;
            string decisionNote = parameters.TryGetValue("p_decision_note", out var dn) && dn != null ? dn.ToString()! : string.Empty;

            int newStatus = approve ? 2 : 3; // 2: Approved, 3: Rejected

            using (var cmd = new SqliteCommand(@"
                UPDATE boa_limit_change_requests
                SET status = @status, checker_user_id = @checker, decision_note = @note, decided_date = @date
                WHERE limit_request_id = @id AND status = 1", conn, tx))
            {
                cmd.Parameters.AddWithValue("@status", newStatus);
                cmd.Parameters.AddWithValue("@checker", checkerUserId);
                cmd.Parameters.AddWithValue("@note", decisionNote);
                cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@id", reqId);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0)
                    throw new Exception("Talep bulunamadı veya zaten karara bağlanmış.");
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

    private void ExecuteReportLostStolen(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            int blockReason = Convert.ToInt32(parameters["p_block_reason"]);
            string description = parameters["p_description"].ToString() ?? string.Empty;
            string? policeReport = parameters.TryGetValue("p_police_report_number", out var prn) && prn != null && prn != DBNull.Value ? prn.ToString() : null;
            string? lastKnownRef = parameters.TryGetValue("p_last_known_transaction_ref", out var lk) && lk != null && lk != DBNull.Value ? lk.ToString() : null;
            bool requestReplacement = parameters.TryGetValue("p_request_replacement", out var rr) && Convert.ToBoolean(rr);
            int? replacementCardId = parameters.TryGetValue("p_replacement_card_id", out var rcid) && rcid != null && rcid != DBNull.Value ? Convert.ToInt32(rcid) : null;
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            using (var cmd = new SqliteCommand(
                "UPDATE boa_cards SET status = 2, block_reason = @reason, blocked_date = @date WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@reason", blockReason);
                cmd.Parameters.AddWithValue("@date", now);
                cmd.Parameters.AddWithValue("@id", cardId);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqliteCommand(@"
                INSERT INTO boa_card_block_history (card_id, block_reason, is_emergency, description, police_report_number, last_known_transaction_ref, replacement_requested, replacement_card_id, user_id, channel, client_ip, created_date)
                VALUES (@cardId, @reason, 1, @desc, @police, @lastRef, @reqRepl, @replCardId, @user, @channel, @ip, @date);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.Parameters.AddWithValue("@reason", blockReason);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@police", (object?)policeReport ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@lastRef", (object?)lastKnownRef ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@reqRepl", requestReplacement ? 1 : 0);
                cmd.Parameters.AddWithValue("@replCardId", (object?)replacementCardId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@user", parameters["p_user_id"]);
                cmd.Parameters.AddWithValue("@channel", parameters["p_channel"]);
                cmd.Parameters.AddWithValue("@ip", parameters["p_client_ip"]);
                cmd.Parameters.AddWithValue("@date", now);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqliteCommand(@"
                INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip, log_date)
                VALUES (@cardId, 'LOST_STOLEN_BLOCK', '1', '2', @desc, @user, @channel, @ip, @date);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@user", parameters["p_user_id"]);
                cmd.Parameters.AddWithValue("@channel", parameters["p_channel"]);
                cmd.Parameters.AddWithValue("@ip", parameters["p_client_ip"]);
                cmd.Parameters.AddWithValue("@date", now);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    private int ExecuteVoidActiveAuths(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        int cardId = Convert.ToInt32(parameters["p_card_id"]);
        string reason = parameters["p_reason"].ToString() ?? "UNKNOWN";
        using var cmd = new SqliteCommand(@"
            UPDATE boa_authorizations SET status = 3, description = description || ' | AUTO_VOID: ' || @reason
            WHERE card_id = @id AND status = 1;
            SELECT changes();", conn);
        cmd.Parameters.AddWithValue("@id", cardId);
        cmd.Parameters.AddWithValue("@reason", reason);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private void ExecuteCancelCard(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            int cancelReason = Convert.ToInt32(parameters["p_cancellation_reason"]);
            string description = parameters["p_description"].ToString() ?? string.Empty;
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            using (var cmd = new SqliteCommand(
                "UPDATE boa_cards SET status = 3, cancelled_date = @date, cancellation_reason = @reason WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@reason", cancelReason);
                cmd.Parameters.AddWithValue("@date", now);
                cmd.Parameters.AddWithValue("@id", cardId);
                cmd.ExecuteNonQuery();
            }
            using (var cmd = new SqliteCommand(@"
                INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip, log_date)
                VALUES (@cardId, 'CARD_CANCELLED', '1', '3', @desc, @user, @channel, @ip, @date);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", cardId);
                cmd.Parameters.AddWithValue("@desc", description);
                cmd.Parameters.AddWithValue("@user", parameters["p_user_id"]);
                cmd.Parameters.AddWithValue("@channel", parameters["p_channel"]);
                cmd.Parameters.AddWithValue("@ip", parameters["p_client_ip"]);
                cmd.Parameters.AddWithValue("@date", now);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    private void ExecuteCreateReplacement(SqliteConnection conn, Dictionary<string, object> parameters)
    {
        using var tx = conn.BeginTransaction();
        try
        {
            string maskedCardNo = parameters["p_card_number"].ToString() ?? "";
            string holderName = parameters["p_card_holder_name"].ToString() ?? "";
            int cardType = Convert.ToInt32(parameters["p_card_type"]);
            int cardBrand = Convert.ToInt32(parameters["p_card_brand"]);
            int cardProduct = Convert.ToInt32(parameters["p_card_product"]);
            DateTime expiry = Convert.ToDateTime(parameters["p_expiry_date"]);
            decimal limit = Convert.ToDecimal(parameters["p_limit"]);
            decimal balance = Convert.ToDecimal(parameters["p_initial_balance"]);
            int oldCardId = Convert.ToInt32(parameters["p_previous_card_id"]);
            string userId = parameters["p_user_id"].ToString() ?? "";
            string channel = parameters["p_channel"].ToString() ?? "";
            string clientIp = parameters["p_client_ip"].ToString() ?? "";
            string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            int customerId, bankAccountId, accountId;
            using (var cmd = new SqliteCommand("SELECT customer_id, bank_account_id, account_id FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", oldCardId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new Exception("Eski kart bulunamadi.");
                customerId = r.GetInt32(0); bankAccountId = r.GetInt32(1); accountId = r.GetInt32(2);
            }

            string insertSql = @"
                INSERT INTO boa_cards (card_number, encrypted_pan, pin_hash, card_holder_name, emboss_name, card_type, card_brand, card_product, expiry_date, status, card_limit, balance, account_id, customer_id, bank_account_id, cvv2_hash, cvv_hash, service_code, track2_data, previous_card_id, created_date)
                VALUES (@num, @enc, @pin, @name, @emboss, @type, @brand, @product, @expiry, @status, @limit, @balance, @accId, @custId, @bankAccId, @cvv2h, @cvvh, @svc, @track2, @prevCardId, @created); SELECT last_insert_rowid();";

            int newCardId;
            using (var cmd = new SqliteCommand(insertSql, conn, tx))
            {
                cmd.Parameters.AddWithValue("@num", maskedCardNo);
                cmd.Parameters.AddWithValue("@enc", parameters["p_encrypted_pan"]);
                cmd.Parameters.AddWithValue("@pin", DBNull.Value);
                cmd.Parameters.AddWithValue("@name", holderName);
                cmd.Parameters.AddWithValue("@emboss", parameters.TryGetValue("p_emboss_name", out var emb) ? emb?.ToString() ?? "" : "");
                cmd.Parameters.AddWithValue("@type", cardType);
                cmd.Parameters.AddWithValue("@brand", cardBrand);
                cmd.Parameters.AddWithValue("@product", cardProduct);
                cmd.Parameters.AddWithValue("@expiry", expiry.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@status", 4);
                cmd.Parameters.AddWithValue("@limit", limit);
                cmd.Parameters.AddWithValue("@balance", balance);
                cmd.Parameters.AddWithValue("@accId", accountId);
                cmd.Parameters.AddWithValue("@custId", customerId);
                cmd.Parameters.AddWithValue("@bankAccId", bankAccountId);
                cmd.Parameters.AddWithValue("@cvv2h", parameters.TryGetValue("p_cvv2_hash", out var cvv2h) ? cvv2h?.ToString() : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@cvvh", parameters.TryGetValue("p_cvv_hash", out var cvvh) ? cvvh?.ToString() : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@svc", parameters.TryGetValue("p_service_code", out var svc) ? svc?.ToString() ?? "201" : "201");
                cmd.Parameters.AddWithValue("@track2", parameters.TryGetValue("p_track2_data", out var trk2) ? trk2?.ToString() : (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@prevCardId", oldCardId);
                cmd.Parameters.AddWithValue("@created", now);
                newCardId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            using (var cmd = new SqliteCommand("UPDATE boa_cards SET replaced_by_card_id = @newId WHERE card_id = @oldId", conn, tx))
            {
                cmd.Parameters.AddWithValue("@newId", newCardId); cmd.Parameters.AddWithValue("@oldId", oldCardId);
                cmd.ExecuteNonQuery();
            }

            using (var cmd = new SqliteCommand(@"
                INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip, log_date)
                VALUES (@cardId, 'CARD_REPLACEMENT_CREATED', NULL, @newVal, 'Kart yenileme/yeniden basim', @user, @channel, @ip, @date);", conn, tx))
            {
                cmd.Parameters.AddWithValue("@cardId", newCardId);
                cmd.Parameters.AddWithValue("@newVal", maskedCardNo);
                cmd.Parameters.AddWithValue("@user", userId);
                cmd.Parameters.AddWithValue("@channel", channel);
                cmd.Parameters.AddWithValue("@ip", clientIp);
                cmd.Parameters.AddWithValue("@date", now);
                cmd.ExecuteNonQuery();
            }
            parameters["p_new_card_id"] = newCardId;
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
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
        // >>> ADIM 12a: Tüm bu metot TEK bir DB transaction (tx) içinde çalışır — kilitleme, limit
        // kontrolü ve ledger yazımı hep aynı transaction'da, ya hep ya hiç (atomik).
        using var tx = conn.BeginTransaction();
        try
        {
            int cardId = Convert.ToInt32(parameters["p_card_id"]);
            int transType = Convert.ToInt32(parameters["p_transaction_type"]);
            decimal amount = Convert.ToDecimal(parameters["p_amount"]);
            string refNo = parameters["p_reference_number"].ToString() ?? string.Empty;
            object merchantId = parameters.TryGetValue("p_merchant_id", out var mid) && mid != null ? mid : DBNull.Value;
            object mcc = parameters.TryGetValue("p_mcc", out var mccVal) && mccVal != null ? mccVal : DBNull.Value;

            int accId = 0;
            int cardType = 1;
            int status = 1;
            decimal limit = 0m;

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
                        limit = Convert.ToDecimal(r.GetValue(3));
                    }
                    else
                    {
                        throw new Exception("Kart Bulunamadi!");
                    }
                }
            }

            // >>> ADIM 12c: Kart aktif mi kontrolü (status=1 → Active). Değilse burada exception fırlar.
            if (status != 1)
            {
                throw new Exception("İşlem Reddedildi: Kart aktif durumda değil!");
            }

            // Defter kayıtlarından güncel bakiyeyi hesapla (SUM(credit) - SUM(debit))
            decimal curBalance = 0m;
            using (var cmd = new SqliteCommand("SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) FROM boa_ledger_entries WHERE account_id = @accId", conn, tx))
            {
                cmd.Parameters.AddWithValue("@accId", accId);
                curBalance = Convert.ToDecimal(cmd.ExecuteScalar());
            }

            // Açık provizyonlardaki (hold) bloke tutarı — limit kontrolünde düşülmesi gerekir.
            decimal held = 0m;
            using (var cmd = new SqliteCommand(
                "SELECT COALESCE(SUM(amount), 0.00) FROM boa_authorizations WHERE card_id = @id AND status = 1", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                held = Convert.ToDecimal(cmd.ExecuteScalar());
            }

            // Finansal Bakiye / Limit kontrolleri
            // Not: Ücret/Faiz (Fee) yansıtması banka tarafından zorlanan bir kayıttır; müşteri işlemi
            // (Harcama/Çekim) gibi bakiye/limit kontrolüne tabi değildir — gecikme faizi kartı zaten
            // limit üzerine taşıyabilir, bu gerçek bankacılıkta da böyledir.
            if (transType == 1 || transType == 2 || transType == 4) // Harcama / Çekim / Ücret-Faiz (Fee)
            {
                if (transType != 4)
                {
                    if (cardType == 1 && (curBalance - held) < amount) // Banka Kartı: bakiye - bloke
                    {
                        throw new Exception($"Yetersiz hesap bakiyesi! Kullanılabilir bakiye: {curBalance - held} TL");
                    }
                    // Kredi kartlarında borç, çift kayıt muhasebe modelinde NEGATİF bakiye olarak tutulur.
                    // Kullanılabilir limit = limit + bakiye - bloke (open-to-buy).
                    else if (cardType == 2 && (limit + curBalance - held) < amount)
                    {
                        throw new Exception($"Yetersiz kart limiti! Kullanılabilir limit: {limit + curBalance - held} TL");
                    }
                }

                // >>> ADIM 12f: Limit kontrolü geçildi — asıl muhasebe kaydı burada oluşuyor. Harcama
                // HER ZAMAN "debit_amount" sütununa yazılır (credit_amount = 0.00); bu satır asla
                // silinmez/güncellenmez, sadece eklenir (defter mantığı).
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

                // Kart ağına (PayCore) bildirilmesi gereken hareketler için (Fee hariç — o hiç ağa
                // çıkmaz), "outbox" satırı ledger kaydıyla AYNI transaction'da (tx) yazılır. Böylece
                // süreç PayCore'u aramadan ÖNCE çökse bile, bu niyet diskte kalıcı olarak durur.
                if (transType == 1 || transType == 2)
                {
                    using (var cmd = new SqliteCommand(@"
                        INSERT INTO boa_paycore_outbox (card_id, amount, bank_reference_number, status, attempt_count, created_date, updated_date)
                        VALUES (@cardId, @amount, @refNo, 1, 0, @date, @date);", conn, tx))
                    {
                        cmd.Parameters.AddWithValue("@cardId", cardId);
                        cmd.Parameters.AddWithValue("@amount", amount);
                        cmd.Parameters.AddWithValue("@refNo", refNo);
                        cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.ExecuteNonQuery();
                    }
                }

                // >>> ADIM 12g: boa_cards.balance önbelleği (cache), ledger'dan yeniden hesaplanarak
                // güncelleniyor — bu alan sadece hızlı okuma için, gerçek kaynak her zaman ledger'dır.
                // Önbellek (cached) bakiye güncelle
                using (var cmd = new SqliteCommand("UPDATE boa_cards SET balance = (SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) FROM boa_ledger_entries WHERE account_id = @accId) WHERE card_id = @cardId", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@accId", accId);
                    cmd.Parameters.AddWithValue("@cardId", cardId);
                    cmd.ExecuteNonQuery();
                }

                // >>> ADIM 12h: tx.Commit() — hem ledger insert hem balance update tek seferde kalıcı
                // oluyor. entryId, çağrı zincirinde ADIM 13'e (SqliteMockProvider case bloğu) geri döner.
                tx.Commit();
                return entryId;
            }
            else if (transType == 3 || transType == 6) // Yatırma/Ödeme (3) veya Ters Kayıt/Reversal (6)
            {
                // Reversal (6): PayCore, bankanın kendi onayından SONRA provizyonu reddederse, az önce
                // yazılan borç (debit) kaydını SİLMEK yerine bunu telafi eden bir alacak (credit) kaydı
                // olarak burada işlenir — ledger'a yazılan hiçbir satır geriye dönük değiştirilmez/silinmez.
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
            decimal amount = Convert.ToDecimal(parameters["p_amount"]);
            string description = parameters["p_description"].ToString() ?? string.Empty;
            string refNo = parameters["p_reference_number"].ToString() ?? string.Empty;
            object merchantId = parameters.TryGetValue("p_merchant_id", out var mid) && mid != null ? mid : DBNull.Value;
            object mcc = parameters.TryGetValue("p_mcc", out var mccVal) && mccVal != null ? mccVal : DBNull.Value;
            string candidateAuthCode = parameters["p_candidate_auth_code"].ToString() ?? string.Empty;
            int? forcedResponseCode = parameters.TryGetValue("p_forced_response_code", out var frc) && frc != null && frc != DBNull.Value ? Convert.ToInt32(frc) : (int?)null;

            int cardType = 1, status = 1;
            decimal limit = 0m;
            DateTime expiryDate;

            using (var cmd = new SqliteCommand("SELECT card_type, status, card_limit, expiry_date FROM boa_cards WHERE card_id = @id", conn, tx))
            {
                cmd.Parameters.AddWithValue("@id", cardId);
                using var r = cmd.ExecuteReader();
                if (!r.Read()) throw new Exception("Kart Bulunamadi!");
                cardType = r.GetInt32(0);
                status = r.GetInt32(1);
                limit = Convert.ToDecimal(r.GetValue(2));
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
                decimal curBalance;
                using (var cmd = new SqliteCommand(
                    "SELECT COALESCE(SUM(l.credit_amount) - SUM(l.debit_amount), 0.00) FROM boa_ledger_entries l JOIN boa_cards c ON l.account_id = c.account_id WHERE c.card_id = @id", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", cardId);
                    curBalance = Convert.ToDecimal(cmd.ExecuteScalar());
                }

                decimal held;
                using (var cmd = new SqliteCommand(
                    "SELECT COALESCE(SUM(amount), 0.00) FROM boa_authorizations WHERE card_id = @id AND status = 1", conn, tx))
                {
                    cmd.Parameters.AddWithValue("@id", cardId);
                    held = Convert.ToDecimal(cmd.ExecuteScalar());
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
            decimal amount;
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
                amount = Convert.ToDecimal(r.GetValue(2));
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

-- =====================================================================================
-- EMLAK KATILIM - BOA CORE BANKING PLATFORM - POSTGRESQL STORED PROCEDURE YAPISI
-- =====================================================================================

-- 1. DEFTER-I KEBIR HESAPLARI TABLOSU (boa_accounts)
-- Çift kayıtlı muhasebe sisteminin temelidir.
CREATE TABLE IF NOT EXISTS boa_accounts (
    account_id SERIAL PRIMARY KEY,
    account_number VARCHAR(30) UNIQUE NOT NULL,       -- GL Hesap No (Örn: GL-CARD-20260701-001)
    account_name VARCHAR(100) NOT NULL,                -- Hesap Adı (Örn: "Ali Yilmaz Kart Hesabi")
    account_type INT NOT NULL,                        -- 1: Card Account, 2: Cash Pool, 3: Merchant Settlement
    created_date TIMESTAMP NOT NULL DEFAULT NOW()
);

-- 1b. MÜŞTERİLER TABLOSU (boa_customers)
-- Kartların bağlı olduğu gerçek kişi kaydı. Kartlar artık bir müşteriye bağlanmadan açılamaz.
CREATE TABLE IF NOT EXISTS boa_customers (
    customer_id SERIAL PRIMARY KEY,
    national_id VARCHAR(11) UNIQUE NOT NULL,           -- T.C. Kimlik No
    full_name VARCHAR(100) NOT NULL,
    phone VARCHAR(20) NULL,
    created_date TIMESTAMP NOT NULL DEFAULT NOW()
);

-- 1c. MÜŞTERİ BANKA HESAPLARI TABLOSU (boa_bank_accounts)
-- Vadesiz/kredi kartı hesabı. Kartlar bu hesaba bağlanır (GL hesabından ayrı, müşteri tarafı hesap).
CREATE TABLE IF NOT EXISTS boa_bank_accounts (
    bank_account_id SERIAL PRIMARY KEY,
    customer_id INT NOT NULL REFERENCES boa_customers(customer_id),
    account_number VARCHAR(50) UNIQUE NOT NULL,        -- IBAN benzeri hesap no (geçici üretim sırasında UUID barındırabilir)
    currency_code VARCHAR(3) NOT NULL DEFAULT 'TRY',
    account_type INT NOT NULL,                         -- 1: Vadesiz (Debit), 2: Kredi Karti Hesabi
    created_date TIMESTAMP NOT NULL DEFAULT NOW()
);

-- 2. KART TABLOSU (boa_cards)
-- PCI-DSS gereğince kart numarası maskeli tutulur. Asıl numara encrypted_pan kolonundadır.
CREATE TABLE IF NOT EXISTS boa_cards (
    card_id SERIAL PRIMARY KEY,
    card_number VARCHAR(19) NOT NULL,                  -- Maskeli Kart No (Örn: 435520******1234)
    encrypted_pan VARCHAR(100) NOT NULL,               -- AES-256 ile şifrelenmiş asıl PAN
    pin_hash VARCHAR(64) NULL,                         -- HSM tarafından şifrelenmiş PIN Block
    card_holder_name VARCHAR(100) NOT NULL,
    card_type INT NOT NULL,                            -- 1: Debit, 2: Credit
    expiry_date TIMESTAMP NOT NULL,
    status INT NOT NULL DEFAULT 1,                     -- 1: Active, 2: Blocked, 3: Cancelled
    card_limit DECIMAL(18,2) NOT NULL DEFAULT 0.00,    -- Kart Limiti
    balance DECIMAL(18,2) NOT NULL DEFAULT 0.00,       -- Muhasebeden senkronize edilen önbellek bakiye
    account_id INT NOT NULL REFERENCES boa_accounts(account_id), -- Karta ait GL Hesabı
    customer_id INT NOT NULL REFERENCES boa_customers(customer_id),         -- Kart hamilinin müşteri kaydı
    bank_account_id INT NOT NULL REFERENCES boa_bank_accounts(bank_account_id), -- Bağlı vadesiz/kredi hesabı
    created_date TIMESTAMP NOT NULL DEFAULT NOW()
);

-- 3. MUHASEBE YEVMİYE KAYITLARI TABLOSU (boa_ledger_entries)
-- Çift kayıt muhasebe sistemi. Doğrudan bakiye güncellemek yerine borç/alacak yevmiye kayıtları atılır.
CREATE TABLE IF NOT EXISTS boa_ledger_entries (
    entry_id SERIAL PRIMARY KEY,
    account_id INT NOT NULL REFERENCES boa_accounts(account_id),
    debit_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00,  -- Borç Tutarı (Hesaptan çıkan para / kredi kartı harcaması)
    credit_amount DECIMAL(18,2) NOT NULL DEFAULT 0.00, -- Alacak Tutarı (Hesaba giren para / limit ödemesi)
    reference_number VARCHAR(50) NOT NULL,             -- İşlem referans no
    transaction_type INT NULL,                         -- TransactionType enum değeri (Refund/Reversal ayrımı için)
    merchant_id VARCHAR(50) NULL,                       -- Üye iş yeri kimliği (ISO 8583 DE42)
    mcc VARCHAR(4) NULL,                                -- Merchant Category Code (ISO 8583 DE18)
    created_date TIMESTAMP NOT NULL DEFAULT NOW()
);

-- 4. DENETİM / AUDIT TABLOSU (boa_card_audit_log)
CREATE TABLE IF NOT EXISTS boa_card_audit_log (
    audit_id SERIAL PRIMARY KEY,
    card_id INT NOT NULL REFERENCES boa_cards(card_id) ON DELETE CASCADE,
    operation_type VARCHAR(50) NOT NULL,               -- 'LIMIT_UPDATE', 'STATUS_CHANGE', 'NEW_CARD'
    old_value VARCHAR(100),
    new_value VARCHAR(100),
    reason VARCHAR(250),
    user_id VARCHAR(50) NOT NULL,
    channel VARCHAR(50) NOT NULL,
    client_ip VARCHAR(50) NOT NULL,
    log_date TIMESTAMP NOT NULL DEFAULT NOW()
);

-- 5. BIN (Issuer Identification Number) TABLOSU
-- Gerçek bankacılıkta BKM'den güncellenen BIN aralıkları tablosunun basitleştirilmiş karşılığı.
CREATE TABLE IF NOT EXISTS boa_bin_table (
    bin_code VARCHAR(6) PRIMARY KEY,
    card_type INT NOT NULL,          -- 1: Debit, 2: Credit
    card_brand VARCHAR(20) NOT NULL
);

INSERT INTO boa_bin_table (bin_code, card_type, card_brand) VALUES
    ('435520', 1, 'VISA'),
    ('543789', 2, 'MASTERCARD')
ON CONFLICT (bin_code) DO NOTHING;

-- 6. PROVİZYON (AUTHORIZATION) TABLOSU
-- Authorize->Capture->Void akışının kalbi. Provizyon alındığında burada bir "hold" kaydı oluşur;
-- yevmiye defterine (boa_ledger_entries) hiçbir şey yazılmaz. Kesinleşme yalnızca Capture ile olur.
CREATE TABLE IF NOT EXISTS boa_authorizations (
    authorization_id SERIAL PRIMARY KEY,
    card_id INT NOT NULL REFERENCES boa_cards(card_id),
    transaction_type INT NOT NULL,
    amount DECIMAL(18,2) NOT NULL,
    response_code INT NOT NULL,            -- ISO 8583 DE39 benzeri (00,05,14,51,54,55,96)
    authorization_code VARCHAR(6) NULL,     -- Onaylandıysa 6 haneli alfanumerik kod
    status INT NOT NULL,                   -- 1: Authorized, 2: Captured, 3: Voided, 4: Declined
    description VARCHAR(250) NOT NULL,
    reference_number VARCHAR(50) NOT NULL,
    merchant_id VARCHAR(50) NULL,
    mcc VARCHAR(4) NULL,
    user_id VARCHAR(50) NOT NULL,
    channel VARCHAR(50) NOT NULL,
    client_ip VARCHAR(50) NOT NULL,
    created_date TIMESTAMP NOT NULL DEFAULT NOW(),
    captured_date TIMESTAMP NULL
);

-- 7. HESAP KESİMİ (STATEMENT) TABLOSU
-- Gün sonu (EOD) batch sürecinin ürettiği kredi kartı ekstreleri.
CREATE TABLE IF NOT EXISTS boa_statements (
    statement_id SERIAL PRIMARY KEY,
    card_id INT NOT NULL REFERENCES boa_cards(card_id),
    statement_date TIMESTAMP NOT NULL,
    due_date TIMESTAMP NOT NULL,
    total_debt DECIMAL(18,2) NOT NULL,
    minimum_payment DECIMAL(18,2) NOT NULL,
    is_paid BOOLEAN NOT NULL DEFAULT FALSE,
    interest_applied BOOLEAN NOT NULL DEFAULT FALSE,
    created_date TIMESTAMP NOT NULL DEFAULT NOW()
);

-- =====================================================================================
-- PL/pgSQL SAKLI YORDAMLARI (STORED PROCEDURES / FUNCTIONS)
-- =====================================================================================

-- PROSEDÜR 1: Yeni Kart Tanımlama (sp_boa_card_create)
CREATE OR REPLACE FUNCTION sp_boa_card_create(
    p_card_number VARCHAR(19),          -- Maskeli Kart No
    p_encrypted_pan VARCHAR(100),       -- Şifreli Kart No
    p_pin_hash VARCHAR(64),             -- HSM PIN Block
    p_card_holder_name VARCHAR(100),
    p_card_type INT,
    p_expiry_date TIMESTAMP,
    p_limit DECIMAL(18,2),
    p_initial_balance DECIMAL(18,2),
    p_user_id VARCHAR(50),
    p_channel VARCHAR(50),
    p_client_ip VARCHAR(50),
    p_national_id VARCHAR(11),          -- Müşteri T.C. Kimlik No (bulunamazsa yeni müşteri açılır)
    p_phone VARCHAR(20)
)
RETURNS TABLE (
    o_card_id INT,
    o_card_number VARCHAR(19),
    o_card_holder_name VARCHAR(100),
    o_card_type INT,
    o_expiry_date TIMESTAMP,
    o_status INT,
    o_limit DECIMAL(18,2),
    o_balance DECIMAL(18,2),
    o_created_date TIMESTAMP,
    o_customer_id INT,
    o_bank_account_id INT,
    o_national_id VARCHAR(11)
) AS $$
DECLARE
    v_new_account_id INT;
    v_new_card_id INT;
    v_acc_num VARCHAR(30);
    v_customer_id INT;
    v_bank_account_id INT;
BEGIN
    -- 0. Müşteriyi T.C. Kimlik No'ya göre bul, yoksa oluştur.
    SELECT customer_id INTO v_customer_id FROM boa_customers WHERE national_id = p_national_id;
    IF v_customer_id IS NULL THEN
        INSERT INTO boa_customers (national_id, full_name, phone)
        VALUES (p_national_id, p_card_holder_name, NULLIF(p_phone, ''))
        RETURNING customer_id INTO v_customer_id;
    END IF;

    -- 0b. Karta bağlı vadesiz/kredi hesabını aç (hesap no, kendi PK'sinden türetilir)
    INSERT INTO boa_bank_accounts (customer_id, account_number, currency_code, account_type)
    VALUES (v_customer_id, 'TEMP-' || gen_random_uuid(), 'TRY', p_card_type)
    RETURNING bank_account_id INTO v_bank_account_id;
    UPDATE boa_bank_accounts SET account_number = 'TR' || lpad(v_bank_account_id::text, 22, '0') WHERE bank_account_id = v_bank_account_id;

    -- 1. Karta ait Defter-i Kebir (GL) Hesabı Aç
    v_acc_num := 'GL-CARD-' || to_char(NOW(), 'YYYYMMDD') || '-' || right(p_card_number, 4);

    INSERT INTO boa_accounts (account_number, account_name, account_type)
    VALUES (v_acc_num, p_card_holder_name || ' Kart Hesabi', 1)
    RETURNING account_id INTO v_new_account_id;

    -- 2. Kartı veritabanına ekle
    INSERT INTO boa_cards (card_number, encrypted_pan, pin_hash, card_holder_name, card_type, expiry_date, status, card_limit, balance, account_id, customer_id, bank_account_id, created_date)
    VALUES (p_card_number, p_encrypted_pan, p_pin_hash, p_card_holder_name, p_card_type, p_expiry_date, 1, p_limit, p_initial_balance, v_new_account_id, v_customer_id, v_bank_account_id, NOW())
    RETURNING card_id INTO v_new_card_id;

    -- 3. İlk Bakiye tanımlaması varsa (Debit Kartlar için) Çift Kayıt Muhasebe Girişi yap
    -- Debit (Kasa): Cash Pool hesabından kart hesabına alacak kaydı atılır
    if p_initial_balance > 0 then
        -- Kart hesabına alacak girişi (alacak bakiyeyi artırır)
        INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number)
        VALUES (v_new_account_id, 0.00, p_initial_balance, 'INITIAL_FUND');
    end if;

    -- 4. Audit log yaz
    INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip)
    VALUES (v_new_card_id, 'NEW_CARD', NULL, p_card_number, 'Kart Ilk Tanimlama ve GL Hesap Acilisi', p_user_id, p_channel, p_client_ip);

    -- 5. Yeni oluşturulan kartı geri döndür
    RETURN QUERY
    SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
           c.customer_id, c.bank_account_id, cu.national_id
    FROM boa_cards c
    JOIN boa_customers cu ON c.customer_id = cu.customer_id
    WHERE c.card_id = v_new_card_id;
END;
$$ LANGUAGE plpgsql;


-- PROSEDÜR 2: Kartları Listeleme (sp_boa_card_get_list)
CREATE OR REPLACE FUNCTION sp_boa_card_get_list(
    p_holder_name VARCHAR(100) DEFAULT NULL,
    p_card_type INT DEFAULT NULL,
    p_status INT DEFAULT NULL,
    p_card_id INT DEFAULT NULL
)
RETURNS TABLE (
    o_card_id INT,
    o_card_number VARCHAR(19),
    o_card_holder_name VARCHAR(100),
    o_card_type INT,
    o_expiry_date TIMESTAMP,
    o_status INT,
    o_limit DECIMAL(18,2),
    o_balance DECIMAL(18,2),
    o_created_date TIMESTAMP,
    o_customer_id INT,
    o_bank_account_id INT,
    o_national_id VARCHAR(11)
) AS $$
BEGIN
    RETURN QUERY
    SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
           c.customer_id, c.bank_account_id, cu.national_id
    FROM boa_cards c
    JOIN boa_customers cu ON c.customer_id = cu.customer_id
    WHERE
        (p_holder_name IS NULL OR c.card_holder_name ILIKE '%' || p_holder_name || '%') AND
        (p_card_type IS NULL OR c.card_type = p_card_type) AND
        (p_status IS NULL OR c.status = p_status) AND
        (p_card_id IS NULL OR c.card_id = p_card_id)
    ORDER BY c.card_id DESC;
END;
$$ LANGUAGE plpgsql;


-- PROSEDÜR 3: Kart Limit Güncelleme (sp_boa_card_update_limit)
-- Pessimistic Lock (FOR UPDATE) eklenmiştir.
CREATE OR REPLACE FUNCTION sp_boa_card_update_limit(
    p_card_id INT,
    p_new_limit DECIMAL(18,2),
    p_user_id VARCHAR(50),
    p_channel VARCHAR(50),
    p_client_ip VARCHAR(50)
)
RETURNS TABLE (
    o_card_id INT,
    o_card_number VARCHAR(19),
    o_card_holder_name VARCHAR(100),
    o_card_type INT,
    o_expiry_date TIMESTAMP,
    o_status INT,
    o_limit DECIMAL(18,2),
    o_balance DECIMAL(18,2),
    o_created_date TIMESTAMP,
    o_customer_id INT,
    o_bank_account_id INT,
    o_national_id VARCHAR(11)
) AS $$
DECLARE
    v_old_limit DECIMAL(18,2);
    v_card_number VARCHAR(19);
BEGIN
    -- Eşzamanlılık Koruması: Kart satırını güncelleme bitene kadar kilitler
    SELECT card_limit, card_number INTO v_old_limit, v_card_number
    FROM boa_cards
    WHERE card_id = p_card_id
    FOR UPDATE;

    -- Limiti güncelle
    UPDATE boa_cards
    SET card_limit = p_new_limit
    WHERE card_id = p_card_id;

    -- Audit log yaz
    INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip)
    VALUES (p_card_id, 'LIMIT_UPDATE', v_old_limit::VARCHAR, p_new_limit::VARCHAR, 'Kart Limit Guncelleme', p_user_id, p_channel, p_client_ip);

    RETURN QUERY
    SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
           c.customer_id, c.bank_account_id, cu.national_id
    FROM boa_cards c
    JOIN boa_customers cu ON c.customer_id = cu.customer_id
    WHERE c.card_id = p_card_id;
END;
$$ LANGUAGE plpgsql;


-- PROSEDÜR 4: Kart Durumu Değiştirme (sp_boa_card_set_status)
CREATE OR REPLACE FUNCTION sp_boa_card_set_status(
    p_card_id INT,
    p_new_status INT,
    p_reason VARCHAR(250),
    p_user_id VARCHAR(50),
    p_channel VARCHAR(50),
    p_client_ip VARCHAR(50)
)
RETURNS TABLE (
    o_card_id INT,
    o_card_number VARCHAR(19),
    o_card_holder_name VARCHAR(100),
    o_card_type INT,
    o_expiry_date TIMESTAMP,
    o_status INT,
    o_limit DECIMAL(18,2),
    o_balance DECIMAL(18,2),
    o_created_date TIMESTAMP,
    o_customer_id INT,
    o_bank_account_id INT,
    o_national_id VARCHAR(11)
) AS $$
DECLARE
    v_old_status INT;
    v_card_number VARCHAR(19);
BEGIN
    -- Kart kilitleme
    SELECT status, card_number INTO v_old_status, v_card_number
    FROM boa_cards
    WHERE card_id = p_card_id
    FOR UPDATE;

    UPDATE boa_cards
    SET status = p_new_status
    WHERE card_id = p_card_id;

    -- Audit log yaz
    INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip)
    VALUES (p_card_id, 'STATUS_CHANGE', v_old_status::VARCHAR, p_new_status::VARCHAR, p_reason, p_user_id, p_channel, p_client_ip);

    RETURN QUERY
    SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
           c.customer_id, c.bank_account_id, cu.national_id
    FROM boa_cards c
    JOIN boa_customers cu ON c.customer_id = cu.customer_id
    WHERE c.card_id = p_card_id;
END;
$$ LANGUAGE plpgsql;


-- PROSEDÜR 5: Çift Kayıt Muhasebe ve Kart Hareketleri (sp_boa_card_create_transaction)
-- Bu prosedür doğrudan bakiye alanını ezmez. Muhasebe defteri borç/alacak kayıtlarını atar, 
-- ardından bakiyeyi defter kayıtlarının toplamıyla senkronize eder.
CREATE OR REPLACE FUNCTION sp_boa_card_create_transaction(
    p_card_id INT,
    p_transaction_type INT,     -- 1: Harcama, 2: Para Cekme, 3: Para Yatirma
    p_amount DECIMAL(18,2),
    p_description VARCHAR(250),
    p_reference_number VARCHAR(50),
    p_user_id VARCHAR(50),
    p_channel VARCHAR(50),
    p_client_ip VARCHAR(50),
    p_merchant_id VARCHAR(50) DEFAULT NULL,
    p_mcc VARCHAR(4) DEFAULT NULL
)
RETURNS TABLE (
    o_transaction_id INT,
    o_card_id INT,
    o_transaction_type INT,
    o_amount DECIMAL(18,2),
    o_description VARCHAR(250),
    o_transaction_date TIMESTAMP,
    o_reference_number VARCHAR(50),
    o_merchant_id VARCHAR(50),
    o_mcc VARCHAR(4)
) AS $$
DECLARE
    v_account_id INT;
    v_card_type INT;
    v_status INT;
    v_limit DECIMAL(18,2);
    v_cur_balance DECIMAL(18,2);
    v_new_entry_id INT;
BEGIN
    -- 1. Kartı ve Hesabı kilitle (Pessimistic Lock)
    SELECT account_id, card_type, status, card_limit INTO v_account_id, v_card_type, v_status, v_limit
    FROM boa_cards
    WHERE card_id = p_card_id
    FOR UPDATE;

    -- Kart durum kontrolü
    if v_status <> 1 then
        RAISE EXCEPTION 'Kart aktif durumda degil! Islem iptal edildi.';
    end if;

    -- 2. Defter kayıtlarının toplamından güncel bakiye/kullanılabilir tutarı hesapla
    SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) INTO v_cur_balance
    FROM boa_ledger_entries
    WHERE account_id = v_account_id;

    -- 3. Finansal Kontroller ve Çift Kayıt Muhasebe (Double-Entry Ledger) Kaydı Girişi
    -- Not: Ücret/Faiz (Fee, tip 4) banka tarafından zorlanan bir kayıttır; müşteri işlemi gibi
    -- bakiye/limit kontrolüne tabi değildir (gecikme faizi kartı limit üzerine taşıyabilir).
    if p_transaction_type = 1 or p_transaction_type = 2 or p_transaction_type = 4 then
        -- A. Para Çıkışı / Harcama / Ücret: Kart hesabından BORÇ kaydı atılır.
        -- Bakiye Kontrolü (Debit Kartlar için) veya Limit Kontrolü (Kredi Kartları için)
        if p_transaction_type <> 4 then
            -- Kredi kartlarında borç NEGATİF bakiye olarak tutulur; kullanılabilir limit = limit + bakiye'dir.
            if v_card_type = 1 and (v_cur_balance < p_amount) then
                RAISE EXCEPTION 'Hesap bakiyesi yetersiz! Kullanilabilir bakiye: % TL', v_cur_balance;
            elsif v_card_type = 2 and ((v_limit + v_cur_balance) < p_amount) then
                RAISE EXCEPTION 'Kart limiti yetersiz! Kullanilabilir limit: % TL', (v_limit + v_cur_balance);
            end if;
        end if;

        -- Çift Kayıt Muhasebe: Kart Hesabına BORÇ yazılır (Debit)
        INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc)
        VALUES (v_account_id, p_amount, 0.00, p_reference_number, p_transaction_type, p_merchant_id, p_mcc)
        RETURNING entry_id INTO v_new_entry_id;

    elsif p_transaction_type = 3 then
        -- B. Para Girişi / Ödeme: Kart hesabına ALACAK kaydı atılır.
        INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc)
        VALUES (v_account_id, 0.00, p_amount, p_reference_number, p_transaction_type, p_merchant_id, p_mcc)
        RETURNING entry_id INTO v_new_entry_id;
    end if;

    -- 4. Kart önbellek (cached) bakiye kolonunu son yevmiye kayıtları toplamıyla senkronize et
    UPDATE boa_cards
    SET balance = (
        SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00)
        FROM boa_ledger_entries
        WHERE account_id = v_account_id
    )
    WHERE card_id = p_card_id;

    -- 5. İşlem hareket kaydını dön (Raporlama ve UI ekranı için)
    -- Not: boa_ledger_entries tablosu ana yevmiye defteridir. Dönen transaction_id bu kaydın referansıdır.
    RETURN QUERY
    SELECT v_new_entry_id, p_card_id, p_transaction_type, p_amount, p_description, LOCALTIMESTAMP, p_reference_number, p_merchant_id, p_mcc;
END;
$$ LANGUAGE plpgsql;


-- PROSEDÜR 6: Kart Hareketleri Ekstresi (sp_boa_card_get_transactions)
CREATE OR REPLACE FUNCTION sp_boa_card_get_transactions(
    p_card_id INT
)
RETURNS TABLE (
    o_transaction_id INT,
    o_card_id INT,
    o_transaction_type INT,
    o_amount DECIMAL(18,2),
    o_description VARCHAR(250),
    o_transaction_date TIMESTAMP,
    o_reference_number VARCHAR(50),
    o_merchant_id VARCHAR(50),
    o_mcc VARCHAR(4)
) AS $$
DECLARE
    v_account_id INT;
BEGIN
    SELECT account_id INTO v_account_id FROM boa_cards WHERE card_id = p_card_id;

    -- Yevmiye defterindeki kayıtları hareket formatında istemciye dönüyoruz
    RETURN QUERY
    SELECT 
        entry_id, 
        p_card_id,
        COALESCE(transaction_type, CASE
            WHEN debit_amount > 0 AND reference_number <> 'INITIAL_FUND' THEN 1 -- Harcama
            WHEN credit_amount > 0 AND reference_number = 'INITIAL_FUND' THEN 3  -- İlk para yükleme
            ELSE 3 -- Ödeme / Yatırma
        END) AS transaction_type,
        CASE WHEN debit_amount > 0 THEN debit_amount ELSE credit_amount END AS amount,
        CASE WHEN debit_amount > 0 THEN 'BORC - Kartli Harcama' ELSE 'ALACAK - Para Yatirma/Odeme' END::VARCHAR(250) AS description,
        created_date,
        reference_number,
        merchant_id,
        mcc
    FROM boa_ledger_entries
    WHERE account_id = v_account_id
    ORDER BY entry_id DESC;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 7: Şifreli Kart ve PIN Detaylarını Sorgulama
CREATE OR REPLACE FUNCTION sp_boa_card_get_secure_details(
    p_card_id INT
)
RETURNS TABLE (
    o_card_id INT,
    o_card_number VARCHAR(19),
    o_encrypted_pan VARCHAR(100),
    o_pin_hash VARCHAR(64)
) AS $$
BEGIN
    RETURN QUERY
    SELECT card_id, card_number, encrypted_pan, pin_hash
    FROM boa_cards
    WHERE card_id = p_card_id;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 8: Kart Türüne Göre BIN Kodu Sorgulama (sp_boa_bin_lookup)
CREATE OR REPLACE FUNCTION sp_boa_bin_lookup(
    p_card_type INT
)
RETURNS TABLE (
    o_bin_code VARCHAR(6),
    o_card_brand VARCHAR(20)
) AS $$
BEGIN
    RETURN QUERY
    SELECT bin_code, card_brand
    FROM boa_bin_table
    WHERE card_type = p_card_type
    LIMIT 1;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 9: Provizyon Oluşturma (sp_boa_auth_create)
-- p_forced_response_code verilmişse (örn. PIN hatası nedeniyle 55) bakiye/limit kontrolü hiç
-- yapılmadan doğrudan reddedilir; aksi halde kart durumu, son kullanma tarihi ve (mevcut bloke
-- edilmiş diğer provizyonlar da dahil edilerek) bakiye/limit tek bir kilitli işlemde kontrol edilir.
CREATE OR REPLACE FUNCTION sp_boa_auth_create(
    p_card_id INT,
    p_transaction_type INT,
    p_amount DECIMAL(18,2),
    p_description VARCHAR(250),
    p_reference_number VARCHAR(50),
    p_merchant_id VARCHAR(50),
    p_mcc VARCHAR(4),
    p_candidate_auth_code VARCHAR(6),
    p_forced_response_code INT,
    p_user_id VARCHAR(50),
    p_channel VARCHAR(50),
    p_client_ip VARCHAR(50)
)
RETURNS TABLE (
    o_authorization_id INT,
    o_card_id INT,
    o_transaction_type INT,
    o_amount DECIMAL(18,2),
    o_response_code INT,
    o_authorization_code VARCHAR(6),
    o_status INT,
    o_description VARCHAR(250),
    o_reference_number VARCHAR(50),
    o_merchant_id VARCHAR(50),
    o_mcc VARCHAR(4),
    o_created_date TIMESTAMP
) AS $$
DECLARE
    v_card_type INT;
    v_card_status INT;
    v_card_limit DECIMAL(18,2);
    v_expiry_date TIMESTAMP;
    v_cur_balance DECIMAL(18,2);
    v_held DECIMAL(18,2);
    v_response_code INT;
    v_auth_status INT;
    v_final_auth_code VARCHAR(6);
    v_new_auth_id INT;
BEGIN
    SELECT card_type, status, card_limit, expiry_date INTO v_card_type, v_card_status, v_card_limit, v_expiry_date
    FROM boa_cards
    WHERE card_id = p_card_id
    FOR UPDATE;

    IF p_forced_response_code IS NOT NULL THEN
        v_response_code := p_forced_response_code;
        v_auth_status := 4; -- Declined
        v_final_auth_code := NULL;
    ELSIF v_card_status <> 1 THEN
        v_response_code := 5; -- Do Not Honor
        v_auth_status := 4;
        v_final_auth_code := NULL;
    ELSIF v_expiry_date < NOW() THEN
        v_response_code := 54; -- Expired Card
        v_auth_status := 4;
        v_final_auth_code := NULL;
    ELSE
        SELECT COALESCE(SUM(l.credit_amount) - SUM(l.debit_amount), 0.00) INTO v_cur_balance
        FROM boa_ledger_entries l
        JOIN boa_cards c ON l.account_id = c.account_id
        WHERE c.card_id = p_card_id;

        SELECT COALESCE(SUM(amount), 0.00) INTO v_held
        FROM boa_authorizations
        WHERE card_id = p_card_id AND status = 1;

        IF (v_card_type = 1 AND (v_cur_balance - v_held) < p_amount) OR
           (v_card_type = 2 AND (v_card_limit + v_cur_balance - v_held) < p_amount) THEN
            v_response_code := 51; -- Insufficient Funds
            v_auth_status := 4;
            v_final_auth_code := NULL;
        ELSE
            v_response_code := 0; -- Approved
            v_auth_status := 1; -- Authorized (hold)
            v_final_auth_code := p_candidate_auth_code;
        END IF;
    END IF;

    INSERT INTO boa_authorizations (card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, user_id, channel, client_ip)
    VALUES (p_card_id, p_transaction_type, p_amount, v_response_code, v_final_auth_code, v_auth_status, p_description, p_reference_number, p_merchant_id, p_mcc, p_user_id, p_channel, p_client_ip)
    RETURNING authorization_id INTO v_new_auth_id;

    RETURN QUERY
    SELECT authorization_id, card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, created_date
    FROM boa_authorizations
    WHERE authorization_id = v_new_auth_id;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 10: Provizyonu Kesinleştirme (sp_boa_auth_capture)
CREATE OR REPLACE FUNCTION sp_boa_auth_capture(
    p_authorization_id INT,
    p_user_id VARCHAR(50),
    p_channel VARCHAR(50),
    p_client_ip VARCHAR(50)
)
RETURNS TABLE (
    o_authorization_id INT,
    o_card_id INT,
    o_transaction_type INT,
    o_amount DECIMAL(18,2),
    o_response_code INT,
    o_authorization_code VARCHAR(6),
    o_status INT,
    o_description VARCHAR(250),
    o_reference_number VARCHAR(50),
    o_merchant_id VARCHAR(50),
    o_mcc VARCHAR(4),
    o_created_date TIMESTAMP
) AS $$
DECLARE
    v_card_id INT;
    v_transaction_type INT;
    v_amount DECIMAL(18,2);
    v_status INT;
    v_reference_number VARCHAR(50);
    v_merchant_id VARCHAR(50);
    v_mcc VARCHAR(4);
    v_account_id INT;
BEGIN
    SELECT card_id, transaction_type, amount, status, reference_number, merchant_id, mcc
    INTO v_card_id, v_transaction_type, v_amount, v_status, v_reference_number, v_merchant_id, v_mcc
    FROM boa_authorizations
    WHERE authorization_id = p_authorization_id
    FOR UPDATE;

    IF v_status <> 1 THEN
        RAISE EXCEPTION 'Bu provizyon Authorized durumda degil, kesinlestirilemez.';
    END IF;

    SELECT account_id INTO v_account_id FROM boa_cards WHERE card_id = v_card_id FOR UPDATE;

    IF v_transaction_type IN (1, 2) THEN
        INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc)
        VALUES (v_account_id, v_amount, 0.00, v_reference_number, v_transaction_type, v_merchant_id, v_mcc);
    ELSE
        INSERT INTO boa_ledger_entries (account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc)
        VALUES (v_account_id, 0.00, v_amount, v_reference_number, v_transaction_type, v_merchant_id, v_mcc);
    END IF;

    UPDATE boa_cards
    SET balance = (SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) FROM boa_ledger_entries WHERE account_id = v_account_id)
    WHERE card_id = v_card_id;

    UPDATE boa_authorizations
    SET status = 2, captured_date = NOW()
    WHERE authorization_id = p_authorization_id;

    RETURN QUERY
    SELECT authorization_id, card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, created_date
    FROM boa_authorizations
    WHERE authorization_id = p_authorization_id;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 11: Provizyonu İptal Etme (sp_boa_auth_void)
CREATE OR REPLACE FUNCTION sp_boa_auth_void(
    p_authorization_id INT,
    p_reason VARCHAR(250)
)
RETURNS TABLE (
    o_authorization_id INT,
    o_card_id INT,
    o_transaction_type INT,
    o_amount DECIMAL(18,2),
    o_response_code INT,
    o_authorization_code VARCHAR(6),
    o_status INT,
    o_description VARCHAR(250),
    o_reference_number VARCHAR(50),
    o_merchant_id VARCHAR(50),
    o_mcc VARCHAR(4),
    o_created_date TIMESTAMP
) AS $$
DECLARE
    v_status INT;
BEGIN
    SELECT status INTO v_status FROM boa_authorizations WHERE authorization_id = p_authorization_id FOR UPDATE;

    IF v_status IS NULL THEN
        RAISE EXCEPTION 'Provizyon bulunamadi.';
    END IF;

    IF v_status <> 1 THEN
        RAISE EXCEPTION 'Bu provizyon Authorized durumda degil, iptal edilemez.';
    END IF;

    UPDATE boa_authorizations
    SET status = 3, description = description || ' | VOID: ' || p_reason
    WHERE authorization_id = p_authorization_id;

    RETURN QUERY
    SELECT authorization_id, card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, created_date
    FROM boa_authorizations
    WHERE authorization_id = p_authorization_id;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 12: Hesap Kesimi (Ekstre) Oluşturma (sp_boa_statement_create)
CREATE OR REPLACE FUNCTION sp_boa_statement_create(
    p_card_id INT,
    p_statement_date TIMESTAMP,
    p_due_date TIMESTAMP,
    p_total_debt DECIMAL(18,2),
    p_minimum_payment DECIMAL(18,2)
)
RETURNS TABLE (
    o_statement_id INT,
    o_card_id INT,
    o_statement_date TIMESTAMP,
    o_due_date TIMESTAMP,
    o_total_debt DECIMAL(18,2),
    o_minimum_payment DECIMAL(18,2),
    o_is_paid BOOLEAN,
    o_interest_applied BOOLEAN,
    o_created_date TIMESTAMP
) AS $$
DECLARE
    v_new_id INT;
BEGIN
    INSERT INTO boa_statements (card_id, statement_date, due_date, total_debt, minimum_payment)
    VALUES (p_card_id, p_statement_date, p_due_date, p_total_debt, p_minimum_payment)
    RETURNING statement_id INTO v_new_id;

    RETURN QUERY
    SELECT statement_id, card_id, statement_date, due_date, total_debt, minimum_payment, is_paid, interest_applied, created_date
    FROM boa_statements
    WHERE statement_id = v_new_id;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 13: Bir Kartın Ödenmemiş (Açık) Son Ekstresini Sorgulama (sp_boa_statement_get_open)
CREATE OR REPLACE FUNCTION sp_boa_statement_get_open(
    p_card_id INT
)
RETURNS TABLE (
    o_statement_id INT,
    o_card_id INT,
    o_statement_date TIMESTAMP,
    o_due_date TIMESTAMP,
    o_total_debt DECIMAL(18,2),
    o_minimum_payment DECIMAL(18,2),
    o_is_paid BOOLEAN,
    o_interest_applied BOOLEAN,
    o_created_date TIMESTAMP
) AS $$
BEGIN
    RETURN QUERY
    SELECT statement_id, card_id, statement_date, due_date, total_debt, minimum_payment, is_paid, interest_applied, created_date
    FROM boa_statements
    WHERE card_id = p_card_id AND is_paid = FALSE
    ORDER BY statement_id DESC
    LIMIT 1;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 14: Bir Karta Ait Tüm Ekstreleri Listeleme (sp_boa_statement_get_list)
CREATE OR REPLACE FUNCTION sp_boa_statement_get_list(
    p_card_id INT
)
RETURNS TABLE (
    o_statement_id INT,
    o_card_id INT,
    o_statement_date TIMESTAMP,
    o_due_date TIMESTAMP,
    o_total_debt DECIMAL(18,2),
    o_minimum_payment DECIMAL(18,2),
    o_is_paid BOOLEAN,
    o_interest_applied BOOLEAN,
    o_created_date TIMESTAMP
) AS $$
BEGIN
    RETURN QUERY
    SELECT statement_id, card_id, statement_date, due_date, total_debt, minimum_payment, is_paid, interest_applied, created_date
    FROM boa_statements
    WHERE card_id = p_card_id
    ORDER BY statement_id DESC;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 15: Ekstreye Gecikme Faizinin İşlendiğini Kaydetme (sp_boa_statement_mark_interest_applied)
CREATE OR REPLACE FUNCTION sp_boa_statement_mark_interest_applied(
    p_statement_id INT
)
RETURNS TABLE (
    o_statement_id INT
) AS $$
BEGIN
    UPDATE boa_statements SET interest_applied = TRUE WHERE statement_id = p_statement_id;
    RETURN QUERY SELECT p_statement_id;
END;
$$ LANGUAGE plpgsql;

-- PROSEDÜR 16: Kart Yenileme (sp_boa_card_renew)
-- Son kullanma tarihi yaklaşan bir kartı yeniler. Gerçek bir bankada bu süreç yeni bir fiziksel kart
-- basımı/gönderimini de tetikler; bu proje yalnızca son kullanma tarihini günceller ve denetim kaydı bırakır.
CREATE OR REPLACE FUNCTION sp_boa_card_renew(
    p_card_id INT,
    p_new_expiry_date TIMESTAMP,
    p_user_id VARCHAR(50),
    p_channel VARCHAR(50),
    p_client_ip VARCHAR(50)
)
RETURNS TABLE (
    o_card_id INT,
    o_card_number VARCHAR(19),
    o_card_holder_name VARCHAR(100),
    o_card_type INT,
    o_expiry_date TIMESTAMP,
    o_status INT,
    o_limit DECIMAL(18,2),
    o_balance DECIMAL(18,2),
    o_created_date TIMESTAMP,
    o_customer_id INT,
    o_bank_account_id INT,
    o_national_id VARCHAR(11)
) AS $$
DECLARE
    v_old_expiry TIMESTAMP;
BEGIN
    SELECT expiry_date INTO v_old_expiry FROM boa_cards WHERE card_id = p_card_id FOR UPDATE;

    UPDATE boa_cards SET expiry_date = p_new_expiry_date WHERE card_id = p_card_id;

    INSERT INTO boa_card_audit_log (card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip)
    VALUES (p_card_id, 'CARD_RENEWED', v_old_expiry::VARCHAR, p_new_expiry_date::VARCHAR, 'Son kullanma tarihi yaklaştığı için otomatik yenileme (EOD Batch)', p_user_id, p_channel, p_client_ip);

    RETURN QUERY
    SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
           c.customer_id, c.bank_account_id, cu.national_id
    FROM boa_cards c
    JOIN boa_customers cu ON c.customer_id = cu.customer_id
    WHERE c.card_id = p_card_id;
END;
$$ LANGUAGE plpgsql;

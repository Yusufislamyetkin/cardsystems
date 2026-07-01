-- =====================================================================================
-- EMLAK KATILIM - BOA CORE BANKING PLATFORM - ORACLE PL/SQL YAPISI (ENTERPRISE SÜRÜM)
-- =====================================================================================

-- 1. SEQUENCES (OTOMATIK ARTAN ID'LER)
BEGIN
    EXECUTE IMMEDIATE 'DROP SEQUENCE SEQ_BOA_ACCOUNTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE SEQUENCE SEQ_BOA_ACCOUNTS START WITH 1 INCREMENT BY 1;

BEGIN
    EXECUTE IMMEDIATE 'DROP SEQUENCE SEQ_BOA_CARDS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE SEQUENCE SEQ_BOA_CARDS START WITH 1 INCREMENT BY 1;

BEGIN
    EXECUTE IMMEDIATE 'DROP SEQUENCE SEQ_BOA_LEDGER';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE SEQUENCE SEQ_BOA_LEDGER START WITH 1 INCREMENT BY 1;

BEGIN
    EXECUTE IMMEDIATE 'DROP SEQUENCE SEQ_BOA_AUDIT';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE SEQUENCE SEQ_BOA_AUDIT START WITH 1 INCREMENT BY 1;

BEGIN
    EXECUTE IMMEDIATE 'DROP SEQUENCE SEQ_BOA_CUSTOMERS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE SEQUENCE SEQ_BOA_CUSTOMERS START WITH 1 INCREMENT BY 1;

BEGIN
    EXECUTE IMMEDIATE 'DROP SEQUENCE SEQ_BOA_BANK_ACCOUNTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE SEQUENCE SEQ_BOA_BANK_ACCOUNTS START WITH 1 INCREMENT BY 1;

BEGIN
    EXECUTE IMMEDIATE 'DROP SEQUENCE SEQ_BOA_AUTHORIZATIONS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE SEQUENCE SEQ_BOA_AUTHORIZATIONS START WITH 1 INCREMENT BY 1;

BEGIN
    EXECUTE IMMEDIATE 'DROP SEQUENCE SEQ_BOA_STATEMENTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE SEQUENCE SEQ_BOA_STATEMENTS START WITH 1 INCREMENT BY 1;


-- 2. TABLO TANIMLAMALARI

-- A. DEFTER-I KEBIR HESAPLARI TABLOSU (boa_accounts)
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_ledger_entries CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_cards CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_bank_accounts CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_customers CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_accounts CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE TABLE boa_accounts (
    account_id NUMBER(10) PRIMARY KEY,
    account_number VARCHAR2(30) UNIQUE NOT NULL,
    account_name VARCHAR2(100) NOT NULL,
    account_type NUMBER(2) NOT NULL,
    created_date TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

-- A2. MÜŞTERİLER TABLOSU (boa_customers)
CREATE TABLE boa_customers (
    customer_id NUMBER(10) PRIMARY KEY,
    national_id VARCHAR2(11) UNIQUE NOT NULL,     -- T.C. Kimlik No
    full_name VARCHAR2(100) NOT NULL,
    phone VARCHAR2(20) NULL,
    created_date TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

-- A3. MÜŞTERİ BANKA HESAPLARI TABLOSU (boa_bank_accounts)
CREATE TABLE boa_bank_accounts (
    bank_account_id NUMBER(10) PRIMARY KEY,
    customer_id NUMBER(10) NOT NULL REFERENCES boa_customers(customer_id),
    account_number VARCHAR2(34) UNIQUE NOT NULL,
    currency_code VARCHAR2(3) DEFAULT 'TRY' NOT NULL,
    account_type NUMBER(2) NOT NULL,               -- 1: Vadesiz (Debit), 2: Kredi Karti Hesabi
    created_date TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

-- B. KART TABLOSU (boa_cards)
CREATE TABLE boa_cards (
    card_id NUMBER(10) PRIMARY KEY,
    card_number VARCHAR2(19) NOT NULL,            -- Maskeli Kart No (435520******1234)
    encrypted_pan VARCHAR2(100) NOT NULL,         -- AES-256 Şifreli Kart No
    pin_hash VARCHAR2(64) NULL,                   -- HSM PIN Block
    card_holder_name VARCHAR2(100) NOT NULL,
    card_type NUMBER(2) NOT NULL,                 -- 1: Debit, 2: Credit
    expiry_date TIMESTAMP NOT NULL,
    status NUMBER(2) DEFAULT 1 NOT NULL,          -- 1: Active, 2: Blocked, 3: Cancelled
    card_limit NUMBER(18,2) DEFAULT 0.00 NOT NULL,
    balance NUMBER(18,2) DEFAULT 0.00 NOT NULL,   -- Önbellek Bakiye
    account_id NUMBER(10) NOT NULL REFERENCES boa_accounts(account_id),
    customer_id NUMBER(10) NOT NULL REFERENCES boa_customers(customer_id),
    bank_account_id NUMBER(10) NOT NULL REFERENCES boa_bank_accounts(bank_account_id),
    created_date TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

-- C. YEVMIYE DEFTERI TABLOSU (boa_ledger_entries)
CREATE TABLE boa_ledger_entries (
    entry_id NUMBER(10) PRIMARY KEY,
    account_id NUMBER(10) NOT NULL REFERENCES boa_accounts(account_id),
    debit_amount NUMBER(18,2) DEFAULT 0.00 NOT NULL,
    credit_amount NUMBER(18,2) DEFAULT 0.00 NOT NULL,
    reference_number VARCHAR2(50) NOT NULL,
    transaction_type NUMBER(2) NULL,               -- TransactionType enum değeri (Refund/Reversal ayrımı için)
    merchant_id VARCHAR2(50) NULL,
    mcc VARCHAR2(4) NULL,
    created_date TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

-- D. AUDIT LOG TABLOSU
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_card_audit_log CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE TABLE boa_card_audit_log (
    audit_id NUMBER(10) PRIMARY KEY,
    card_id NUMBER(10) NOT NULL,
    operation_type VARCHAR2(50) NOT NULL,
    old_value VARCHAR2(100),
    new_value VARCHAR2(100),
    reason VARCHAR2(250),
    user_id VARCHAR2(50) NOT NULL,
    channel VARCHAR2(50) NOT NULL,
    client_ip VARCHAR2(50) NOT NULL,
    log_date TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);

-- E. BIN (Issuer Identification Number) TABLOSU
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_bin_table CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE TABLE boa_bin_table (
    bin_code VARCHAR2(6) PRIMARY KEY,
    card_type NUMBER(2) NOT NULL,
    card_brand VARCHAR2(20) NOT NULL
);
INSERT INTO boa_bin_table (bin_code, card_type, card_brand) VALUES ('435520', 1, 'VISA');
INSERT INTO boa_bin_table (bin_code, card_type, card_brand) VALUES ('543789', 2, 'MASTERCARD');
COMMIT;

-- F. PROVİZYON (AUTHORIZATION) TABLOSU
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_authorizations CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE TABLE boa_authorizations (
    authorization_id NUMBER(10) PRIMARY KEY,
    card_id NUMBER(10) NOT NULL REFERENCES boa_cards(card_id),
    transaction_type NUMBER(2) NOT NULL,
    amount NUMBER(18,2) NOT NULL,
    response_code NUMBER(3) NOT NULL,
    authorization_code VARCHAR2(6) NULL,
    status NUMBER(2) NOT NULL,
    description VARCHAR2(250) NOT NULL,
    reference_number VARCHAR2(50) NOT NULL,
    merchant_id VARCHAR2(50) NULL,
    mcc VARCHAR2(4) NULL,
    user_id VARCHAR2(50) NOT NULL,
    channel VARCHAR2(50) NOT NULL,
    client_ip VARCHAR2(50) NOT NULL,
    created_date TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    captured_date TIMESTAMP NULL
);

-- G. HESAP KESİMİ (STATEMENT) TABLOSU
BEGIN
    EXECUTE IMMEDIATE 'DROP TABLE boa_statements CASCADE CONSTRAINTS';
EXCEPTION WHEN OTHERS THEN NULL;
END;
/
CREATE TABLE boa_statements (
    statement_id NUMBER(10) PRIMARY KEY,
    card_id NUMBER(10) NOT NULL REFERENCES boa_cards(card_id),
    statement_date TIMESTAMP NOT NULL,
    due_date TIMESTAMP NOT NULL,
    total_debt NUMBER(18,2) NOT NULL,
    minimum_payment NUMBER(18,2) NOT NULL,
    is_paid NUMBER(1) DEFAULT 0 NOT NULL,
    interest_applied NUMBER(1) DEFAULT 0 NOT NULL,
    created_date TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL
);


-- =====================================================================================
-- PL/SQL PAKET ARAYÜZ TANIMI (PACKAGE SPECIFICATION)
-- =====================================================================================
CREATE OR REPLACE PACKAGE PKG_BOA_CARD AS

    -- 1. Yeni Kart Tanımlama ve GL Hesap Açılışı
    PROCEDURE SP_CREATE_CARD(
        p_card_number IN VARCHAR2,
        p_encrypted_pan IN VARCHAR2,
        p_pin_hash IN VARCHAR2,
        p_card_holder_name IN VARCHAR2,
        p_card_type IN NUMBER,
        p_expiry_date IN TIMESTAMP,
        p_limit IN NUMBER,
        p_initial_balance IN NUMBER,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        p_national_id IN VARCHAR2,
        p_phone IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 2. Kartları Listeleme
    PROCEDURE SP_GET_CARD_LIST(
        p_holder_name IN VARCHAR2 DEFAULT NULL,
        p_card_type IN NUMBER DEFAULT NULL,
        p_status IN NUMBER DEFAULT NULL,
        p_card_id IN NUMBER DEFAULT NULL,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 3. Kart Limiti Güncelleme (Locking)
    PROCEDURE SP_UPDATE_LIMIT(
        p_card_id IN NUMBER,
        p_new_limit IN NUMBER,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 4. Kart Durumu Değiştirme (Locking)
    PROCEDURE SP_SET_CARD_STATUS(
        p_card_id IN NUMBER,
        p_new_status IN NUMBER,
        p_reason IN VARCHAR2,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 5. Çift Kayıt Muhasebe ve Kart İşlemi Oluşturma
    PROCEDURE SP_CREATE_TRANSACTION(
        p_card_id IN NUMBER,
        p_transaction_type IN NUMBER,
        p_amount IN NUMBER,
        p_description IN VARCHAR2,
        p_reference_number IN VARCHAR2,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        p_merchant_id IN VARCHAR2 DEFAULT NULL,
        p_mcc IN VARCHAR2 DEFAULT NULL,
        o_trans_cursor OUT SYS_REFCURSOR
    );

    -- 6. Yevmiye Hareketleri Ekstresi
    PROCEDURE SP_GET_TRANSACTIONS(
        p_card_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 7. Şifreli Kart ve PIN Detaylarını Sorgulama
    PROCEDURE SP_GET_SECURE_DETAILS(
        p_card_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 8. Kart Türüne Göre BIN Kodu Sorgulama
    PROCEDURE SP_BIN_LOOKUP(
        p_card_type IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 9. Provizyon Oluşturma
    PROCEDURE SP_AUTH_CREATE(
        p_card_id IN NUMBER,
        p_transaction_type IN NUMBER,
        p_amount IN NUMBER,
        p_description IN VARCHAR2,
        p_reference_number IN VARCHAR2,
        p_merchant_id IN VARCHAR2,
        p_mcc IN VARCHAR2,
        p_candidate_auth_code IN VARCHAR2,
        p_forced_response_code IN NUMBER,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 10. Provizyonu Kesinleştirme (Capture)
    PROCEDURE SP_AUTH_CAPTURE(
        p_authorization_id IN NUMBER,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 11. Provizyonu İptal Etme (Void)
    PROCEDURE SP_AUTH_VOID(
        p_authorization_id IN NUMBER,
        p_reason IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 12. Hesap Kesimi (Ekstre) Oluşturma
    PROCEDURE SP_STATEMENT_CREATE(
        p_card_id IN NUMBER,
        p_statement_date IN TIMESTAMP,
        p_due_date IN TIMESTAMP,
        p_total_debt IN NUMBER,
        p_minimum_payment IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 13. Bir Kartın Ödenmemiş (Açık) Son Ekstresini Sorgulama
    PROCEDURE SP_STATEMENT_GET_OPEN(
        p_card_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 14. Bir Karta Ait Tüm Ekstreleri Listeleme
    PROCEDURE SP_STATEMENT_GET_LIST(
        p_card_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 15. Ekstreye Gecikme Faizinin İşlendiğini Kaydetme
    PROCEDURE SP_STATEMENT_MARK_INTEREST_APPLIED(
        p_statement_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    );

    -- 16. Kart Yenileme
    PROCEDURE SP_CARD_RENEW(
        p_card_id IN NUMBER,
        p_new_expiry_date IN TIMESTAMP,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    );

END PKG_BOA_CARD;
/


-- =====================================================================================
-- PL/SQL PAKET GÖVDESİ (PACKAGE BODY)
-- =====================================================================================
CREATE OR REPLACE PACKAGE BODY PKG_BOA_CARD AS

    -- 1. SP_CREATE_CARD: Kart ve Muhasebe Hesabı Tanımlama
    PROCEDURE SP_CREATE_CARD(
        p_card_number IN VARCHAR2,
        p_encrypted_pan IN VARCHAR2,
        p_pin_hash IN VARCHAR2,
        p_card_holder_name IN VARCHAR2,
        p_card_type IN NUMBER,
        p_expiry_date IN TIMESTAMP,
        p_limit IN NUMBER,
        p_initial_balance IN NUMBER,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        p_national_id IN VARCHAR2,
        p_phone IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_new_account_id NUMBER(10);
        v_new_card_id NUMBER(10);
        v_acc_num VARCHAR2(30);
        v_customer_id NUMBER(10);
        v_bank_account_id NUMBER(10);
    BEGIN
        -- A0. Müşteriyi T.C. Kimlik No'ya göre bul, yoksa oluştur.
        BEGIN
            SELECT customer_id INTO v_customer_id FROM boa_customers WHERE national_id = p_national_id;
        EXCEPTION WHEN NO_DATA_FOUND THEN
            INSERT INTO boa_customers (customer_id, national_id, full_name, phone)
            VALUES (SEQ_BOA_CUSTOMERS.NEXTVAL, p_national_id, p_card_holder_name, p_phone)
            RETURNING customer_id INTO v_customer_id;
        END;

        -- A1. Karta bağlı vadesiz/kredi hesabını aç (hesap no kendi PK'sinden türetilir)
        v_bank_account_id := SEQ_BOA_BANK_ACCOUNTS.NEXTVAL;
        INSERT INTO boa_bank_accounts (bank_account_id, customer_id, account_number, currency_code, account_type)
        VALUES (v_bank_account_id, v_customer_id, 'TR' || LPAD(TO_CHAR(v_bank_account_id), 22, '0'), 'TRY', p_card_type);

        -- A. Karta ait GL Muhasebe Hesabı Aç
        v_acc_num := 'GL-CARD-' || TO_CHAR(SYSDATE, 'YYYYMMDD') || '-' || SUBSTR(p_card_number, -4);

        INSERT INTO boa_accounts (account_id, account_number, account_name, account_type)
        VALUES (SEQ_BOA_ACCOUNTS.NEXTVAL, v_acc_num, p_card_holder_name || ' Kart Hesabi', 1)
        RETURNING account_id INTO v_new_account_id;

        -- B. Kartı Ekle
        INSERT INTO boa_cards (card_id, card_number, encrypted_pan, pin_hash, card_holder_name, card_type, expiry_date, status, card_limit, balance, account_id, customer_id, bank_account_id)
        VALUES (SEQ_BOA_CARDS.NEXTVAL, p_card_number, p_encrypted_pan, p_pin_hash, p_card_holder_name, p_card_type, p_expiry_date, 1, p_limit, p_initial_balance, v_new_account_id, v_customer_id, v_bank_account_id)
        RETURNING card_id INTO v_new_card_id;

        -- C. İlk Bakiye Tanımlaması Varsa Muhasebe Yevmiye Defterine yaz
        IF p_initial_balance > 0 THEN
            INSERT INTO boa_ledger_entries (entry_id, account_id, debit_amount, credit_amount, reference_number)
            VALUES (SEQ_BOA_LEDGER.NEXTVAL, v_new_account_id, 0.00, p_initial_balance, 'INITIAL_FUND');
        END IF;

        -- D. Audit Log
        INSERT INTO boa_card_audit_log (audit_id, card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip)
        VALUES (SEQ_BOA_AUDIT.NEXTVAL, v_new_card_id, 'NEW_CARD', NULL, p_card_number, 'Kart ve GL Hesap Acilisi', p_user_id, p_channel, p_client_ip);

        COMMIT;

        OPEN o_cursor FOR
        SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
               c.customer_id, c.bank_account_id, cu.national_id
        FROM boa_cards c
        JOIN boa_customers cu ON c.customer_id = cu.customer_id
        WHERE c.card_id = v_new_card_id;
    END SP_CREATE_CARD;

    -- 2. SP_GET_CARD_LIST: Kart Sorgulama
    PROCEDURE SP_GET_CARD_LIST(
        p_holder_name IN VARCHAR2 DEFAULT NULL,
        p_card_type IN NUMBER DEFAULT NULL,
        p_status IN NUMBER DEFAULT NULL,
        p_card_id IN NUMBER DEFAULT NULL,
        o_cursor OUT SYS_REFCURSOR
    ) AS
    BEGIN
        OPEN o_cursor FOR
        SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
               c.customer_id, c.bank_account_id, cu.national_id
        FROM boa_cards c
        JOIN boa_customers cu ON c.customer_id = cu.customer_id
        WHERE
            (p_holder_name IS NULL OR UPPER(c.card_holder_name) LIKE '%' || UPPER(p_holder_name) || '%') AND
            (p_card_type IS NULL OR c.card_type = p_card_type) AND
            (p_status IS NULL OR c.status = p_status) AND
            (p_card_id IS NULL OR c.card_id = p_card_id)
        ORDER BY c.card_id DESC;
    END SP_GET_CARD_LIST;

    -- 3. SP_UPDATE_LIMIT: Kart Limiti Güncelleme (Pessimistic Lock)
    PROCEDURE SP_UPDATE_LIMIT(
        p_card_id IN NUMBER,
        p_new_limit IN NUMBER,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_old_limit NUMBER(18,2);
        v_card_number VARCHAR2(19);
    BEGIN
        -- Eşzamanlılığı önlemek için satırı kilitliyoruz (FOR UPDATE)
        SELECT card_limit, card_number INTO v_old_limit, v_card_number
        FROM boa_cards
        WHERE card_id = p_card_id
        FOR UPDATE;

        UPDATE boa_cards
        SET card_limit = p_new_limit
        WHERE card_id = p_card_id;

        INSERT INTO boa_card_audit_log (audit_id, card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip)
        VALUES (SEQ_BOA_AUDIT.NEXTVAL, p_card_id, 'LIMIT_UPDATE', TO_CHAR(v_old_limit), TO_CHAR(p_new_limit), 'Kart Limit Guncelleme', p_user_id, p_channel, p_client_ip);

        COMMIT;

        OPEN o_cursor FOR
        SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
               c.customer_id, c.bank_account_id, cu.national_id
        FROM boa_cards c
        JOIN boa_customers cu ON c.customer_id = cu.customer_id
        WHERE c.card_id = p_card_id;
    END SP_UPDATE_LIMIT;

    -- 4. SP_SET_CARD_STATUS: Kart Durum Değişikliği (Pessimistic Lock)
    PROCEDURE SP_SET_CARD_STATUS(
        p_card_id IN NUMBER,
        p_new_status IN NUMBER,
        p_reason IN VARCHAR2,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_old_status NUMBER(2);
        v_card_number VARCHAR2(19);
    BEGIN
        -- Kilitleme
        SELECT status, card_number INTO v_old_status, v_card_number
        FROM boa_cards
        WHERE card_id = p_card_id
        FOR UPDATE;

        UPDATE boa_cards
        SET status = p_new_status
        WHERE card_id = p_card_id;

        INSERT INTO boa_card_audit_log (audit_id, card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip)
        VALUES (SEQ_BOA_AUDIT.NEXTVAL, p_card_id, 'STATUS_CHANGE', TO_CHAR(v_old_status), TO_CHAR(p_new_status), p_reason, p_user_id, p_channel, p_client_ip);

        COMMIT;

        OPEN o_cursor FOR
        SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
               c.customer_id, c.bank_account_id, cu.national_id
        FROM boa_cards c
        JOIN boa_customers cu ON c.customer_id = cu.customer_id
        WHERE c.card_id = p_card_id;
    END SP_SET_CARD_STATUS;

    -- 5. SP_CREATE_TRANSACTION: Çift Kayıt Muhasebe Yevmiye Girişi ve Kart Bakiye Senkronizasyonu
    PROCEDURE SP_CREATE_TRANSACTION(
        p_card_id IN NUMBER,
        p_transaction_type IN NUMBER,
        p_amount IN NUMBER,
        p_description IN VARCHAR2,
        p_reference_number IN VARCHAR2,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        p_merchant_id IN VARCHAR2 DEFAULT NULL,
        p_mcc IN VARCHAR2 DEFAULT NULL,
        o_trans_cursor OUT SYS_REFCURSOR
    ) AS
        v_account_id NUMBER(10);
        v_card_type NUMBER(2);
        v_status NUMBER(2);
        v_limit NUMBER(18,2);
        v_cur_balance NUMBER(18,2);
        v_new_entry_id NUMBER(10);
    BEGIN
        -- A. Kartı ve Hesabı kilitle (FOR UPDATE)
        SELECT account_id, card_type, status, card_limit INTO v_account_id, v_card_type, v_status, v_limit
        FROM boa_cards
        WHERE card_id = p_card_id
        FOR UPDATE;

        IF v_status <> 1 THEN
            raise_application_error(-20001, 'Kart aktif durumda degil! Islem iptal edildi.');
        END IF;

        -- B. Defter kayıtlarından bakiyeyi doğrula
        SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) INTO v_cur_balance
        FROM boa_ledger_entries
        WHERE account_id = v_account_id;

        -- C. Finansal Kontroller ve Çift Kayıt Girişleri
        -- Not: Ücret/Faiz (Fee, tip 4) banka tarafından zorlanan bir kayıttır; müşteri işlemi gibi
        -- bakiye/limit kontrolüne tabi değildir (gecikme faizi kartı limit üzerine taşıyabilir).
        IF p_transaction_type = 1 OR p_transaction_type = 2 OR p_transaction_type = 4 THEN
            -- Banka Kartı Bakiye, Kredi Kartı Kullanılabilir Limit Kontrolü
            IF p_transaction_type <> 4 THEN
                -- Kredi kartlarında borç NEGATİF bakiye olarak tutulur; kullanılabilir limit = limit + bakiye'dir.
                IF v_card_type = 1 AND (v_cur_balance < p_amount) THEN
                    raise_application_error(-20002, 'Hesap bakiyesi yetersiz! Kullanilabilir bakiye: ' || v_cur_balance || ' TL');
                ELSIF v_card_type = 2 AND ((v_limit + v_cur_balance) < p_amount) THEN
                    raise_application_error(-20003, 'Kart limiti yetersiz! Kullanilabilir limit: ' || (v_limit + v_cur_balance) || ' TL');
                END IF;
            END IF;

            -- Yevmiye Defteri BORÇ kaydı (Debit)
            v_new_entry_id := SEQ_BOA_LEDGER.NEXTVAL;
            INSERT INTO boa_ledger_entries (entry_id, account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc)
            VALUES (v_new_entry_id, v_account_id, p_amount, 0.00, p_reference_number, p_transaction_type, p_merchant_id, p_mcc);

        ELSIF p_transaction_type = 3 THEN
            -- Yevmiye Defteri ALACAK kaydı (Credit)
            v_new_entry_id := SEQ_BOA_LEDGER.NEXTVAL;
            INSERT INTO boa_ledger_entries (entry_id, account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc)
            VALUES (v_new_entry_id, v_account_id, 0.00, p_amount, p_reference_number, p_transaction_type, p_merchant_id, p_mcc);
        END IF;

        -- D. Kart önbellek bakiye kolonunu yevmiye kayıtları toplamıyla güncelle
        UPDATE boa_cards
        SET balance = (
            SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00)
            FROM boa_ledger_entries
            WHERE account_id = v_account_id
        )
        WHERE card_id = p_card_id;

        COMMIT;

        OPEN o_trans_cursor FOR
        SELECT v_new_entry_id AS transaction_id, p_card_id AS card_id, p_transaction_type AS transaction_type, p_amount AS amount, p_description AS description, CAST(SYSTIMESTAMP AS TIMESTAMP) AS transaction_date, p_reference_number AS reference_number, p_merchant_id AS merchant_id, p_mcc AS mcc
        FROM dual;
    END SP_CREATE_TRANSACTION;

    -- 6. SP_GET_TRANSACTIONS: Yevmiye Hareketlerinden Ekstre Çekme
    PROCEDURE SP_GET_TRANSACTIONS(
        p_card_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_account_id NUMBER(10);
    BEGIN
        SELECT account_id INTO v_account_id FROM boa_cards WHERE card_id = p_card_id;

        OPEN o_cursor FOR
        SELECT
            entry_id AS transaction_id,
            p_card_id AS card_id,
            COALESCE(transaction_type, CASE
                WHEN debit_amount > 0 AND reference_number <> 'INITIAL_FUND' THEN 1 -- Harcama
                WHEN credit_amount > 0 AND reference_number = 'INITIAL_FUND' THEN 3  -- İlk para yükleme
                ELSE 3 -- Ödeme / Yatırma
            END) AS transaction_type,
            CASE WHEN debit_amount > 0 THEN debit_amount ELSE credit_amount END AS amount,
            CASE WHEN debit_amount > 0 THEN 'BORC - Kartli Harcama' ELSE 'ALACAK - Para Yatirma/Odeme' END AS description,
            created_date AS transaction_date,
            reference_number,
            merchant_id,
            mcc
        FROM boa_ledger_entries
        WHERE account_id = v_account_id
        ORDER BY entry_id DESC;
    END SP_GET_TRANSACTIONS;

    -- 7. SP_GET_SECURE_DETAILS: Şifreli Kart ve PIN Detaylarını Sorgulama
    PROCEDURE SP_GET_SECURE_DETAILS(
        p_card_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    ) AS
    BEGIN
        OPEN o_cursor FOR
        SELECT card_id, card_number, encrypted_pan, pin_hash
        FROM boa_cards
        WHERE card_id = p_card_id;
    END SP_GET_SECURE_DETAILS;

    -- 8. SP_BIN_LOOKUP: Kart Türüne Göre BIN Kodu Sorgulama
    PROCEDURE SP_BIN_LOOKUP(
        p_card_type IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    ) AS
    BEGIN
        OPEN o_cursor FOR
        SELECT bin_code, card_brand
        FROM boa_bin_table
        WHERE card_type = p_card_type
        FETCH FIRST 1 ROWS ONLY;
    END SP_BIN_LOOKUP;

    -- 9. SP_AUTH_CREATE: Provizyon Oluşturma
    PROCEDURE SP_AUTH_CREATE(
        p_card_id IN NUMBER,
        p_transaction_type IN NUMBER,
        p_amount IN NUMBER,
        p_description IN VARCHAR2,
        p_reference_number IN VARCHAR2,
        p_merchant_id IN VARCHAR2,
        p_mcc IN VARCHAR2,
        p_candidate_auth_code IN VARCHAR2,
        p_forced_response_code IN NUMBER,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_card_type NUMBER(2);
        v_card_status NUMBER(2);
        v_card_limit NUMBER(18,2);
        v_expiry_date TIMESTAMP;
        v_cur_balance NUMBER(18,2);
        v_held NUMBER(18,2);
        v_response_code NUMBER(3);
        v_auth_status NUMBER(2);
        v_final_auth_code VARCHAR2(6);
        v_new_auth_id NUMBER(10);
    BEGIN
        SELECT card_type, status, card_limit, expiry_date INTO v_card_type, v_card_status, v_card_limit, v_expiry_date
        FROM boa_cards
        WHERE card_id = p_card_id
        FOR UPDATE;

        IF p_forced_response_code IS NOT NULL THEN
            v_response_code := p_forced_response_code;
            v_auth_status := 4;
            v_final_auth_code := NULL;
        ELSIF v_card_status <> 1 THEN
            v_response_code := 5;
            v_auth_status := 4;
            v_final_auth_code := NULL;
        ELSIF v_expiry_date < SYSTIMESTAMP THEN
            v_response_code := 54;
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
                v_response_code := 51;
                v_auth_status := 4;
                v_final_auth_code := NULL;
            ELSE
                v_response_code := 0;
                v_auth_status := 1;
                v_final_auth_code := p_candidate_auth_code;
            END IF;
        END IF;

        v_new_auth_id := SEQ_BOA_AUTHORIZATIONS.NEXTVAL;
        INSERT INTO boa_authorizations (authorization_id, card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, user_id, channel, client_ip)
        VALUES (v_new_auth_id, p_card_id, p_transaction_type, p_amount, v_response_code, v_final_auth_code, v_auth_status, p_description, p_reference_number, p_merchant_id, p_mcc, p_user_id, p_channel, p_client_ip);

        COMMIT;

        OPEN o_cursor FOR
        SELECT authorization_id, card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, created_date
        FROM boa_authorizations
        WHERE authorization_id = v_new_auth_id;
    END SP_AUTH_CREATE;

    -- 10. SP_AUTH_CAPTURE: Provizyonu Kesinleştirme
    PROCEDURE SP_AUTH_CAPTURE(
        p_authorization_id IN NUMBER,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_card_id NUMBER(10);
        v_transaction_type NUMBER(2);
        v_amount NUMBER(18,2);
        v_status NUMBER(2);
        v_reference_number VARCHAR2(50);
        v_merchant_id VARCHAR2(50);
        v_mcc VARCHAR2(4);
        v_account_id NUMBER(10);
    BEGIN
        SELECT card_id, transaction_type, amount, status, reference_number, merchant_id, mcc
        INTO v_card_id, v_transaction_type, v_amount, v_status, v_reference_number, v_merchant_id, v_mcc
        FROM boa_authorizations
        WHERE authorization_id = p_authorization_id
        FOR UPDATE;

        IF v_status <> 1 THEN
            raise_application_error(-20010, 'Bu provizyon Authorized durumda degil, kesinlestirilemez.');
        END IF;

        SELECT account_id INTO v_account_id FROM boa_cards WHERE card_id = v_card_id FOR UPDATE;

        IF v_transaction_type IN (1, 2) THEN
            INSERT INTO boa_ledger_entries (entry_id, account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc)
            VALUES (SEQ_BOA_LEDGER.NEXTVAL, v_account_id, v_amount, 0.00, v_reference_number, v_transaction_type, v_merchant_id, v_mcc);
        ELSE
            INSERT INTO boa_ledger_entries (entry_id, account_id, debit_amount, credit_amount, reference_number, transaction_type, merchant_id, mcc)
            VALUES (SEQ_BOA_LEDGER.NEXTVAL, v_account_id, 0.00, v_amount, v_reference_number, v_transaction_type, v_merchant_id, v_mcc);
        END IF;

        UPDATE boa_cards
        SET balance = (SELECT COALESCE(SUM(credit_amount) - SUM(debit_amount), 0.00) FROM boa_ledger_entries WHERE account_id = v_account_id)
        WHERE card_id = v_card_id;

        UPDATE boa_authorizations
        SET status = 2, captured_date = SYSTIMESTAMP
        WHERE authorization_id = p_authorization_id;

        COMMIT;

        OPEN o_cursor FOR
        SELECT authorization_id, card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, created_date
        FROM boa_authorizations
        WHERE authorization_id = p_authorization_id;
    END SP_AUTH_CAPTURE;

    -- 11. SP_AUTH_VOID: Provizyonu İptal Etme
    PROCEDURE SP_AUTH_VOID(
        p_authorization_id IN NUMBER,
        p_reason IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_status NUMBER(2);
    BEGIN
        SELECT status INTO v_status FROM boa_authorizations WHERE authorization_id = p_authorization_id FOR UPDATE;

        IF v_status <> 1 THEN
            raise_application_error(-20011, 'Bu provizyon Authorized durumda degil, iptal edilemez.');
        END IF;

        UPDATE boa_authorizations
        SET status = 3, description = description || ' | VOID: ' || p_reason
        WHERE authorization_id = p_authorization_id;

        COMMIT;

        OPEN o_cursor FOR
        SELECT authorization_id, card_id, transaction_type, amount, response_code, authorization_code, status, description, reference_number, merchant_id, mcc, created_date
        FROM boa_authorizations
        WHERE authorization_id = p_authorization_id;
    END SP_AUTH_VOID;

    -- 12. SP_STATEMENT_CREATE: Hesap Kesimi (Ekstre) Oluşturma
    PROCEDURE SP_STATEMENT_CREATE(
        p_card_id IN NUMBER,
        p_statement_date IN TIMESTAMP,
        p_due_date IN TIMESTAMP,
        p_total_debt IN NUMBER,
        p_minimum_payment IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_new_id NUMBER(10);
    BEGIN
        v_new_id := SEQ_BOA_STATEMENTS.NEXTVAL;
        INSERT INTO boa_statements (statement_id, card_id, statement_date, due_date, total_debt, minimum_payment)
        VALUES (v_new_id, p_card_id, p_statement_date, p_due_date, p_total_debt, p_minimum_payment);

        COMMIT;

        OPEN o_cursor FOR
        SELECT statement_id, card_id, statement_date, due_date, total_debt, minimum_payment, is_paid, interest_applied, created_date
        FROM boa_statements
        WHERE statement_id = v_new_id;
    END SP_STATEMENT_CREATE;

    -- 13. SP_STATEMENT_GET_OPEN: Bir Kartın Ödenmemiş (Açık) Son Ekstresini Sorgulama
    PROCEDURE SP_STATEMENT_GET_OPEN(
        p_card_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    ) AS
    BEGIN
        OPEN o_cursor FOR
        SELECT statement_id, card_id, statement_date, due_date, total_debt, minimum_payment, is_paid, interest_applied, created_date
        FROM boa_statements
        WHERE card_id = p_card_id AND is_paid = 0
        ORDER BY statement_id DESC
        FETCH FIRST 1 ROWS ONLY;
    END SP_STATEMENT_GET_OPEN;

    -- 14. SP_STATEMENT_GET_LIST: Bir Karta Ait Tüm Ekstreleri Listeleme
    PROCEDURE SP_STATEMENT_GET_LIST(
        p_card_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    ) AS
    BEGIN
        OPEN o_cursor FOR
        SELECT statement_id, card_id, statement_date, due_date, total_debt, minimum_payment, is_paid, interest_applied, created_date
        FROM boa_statements
        WHERE card_id = p_card_id
        ORDER BY statement_id DESC;
    END SP_STATEMENT_GET_LIST;

    -- 15. SP_STATEMENT_MARK_INTEREST_APPLIED: Ekstreye Gecikme Faizinin İşlendiğini Kaydetme
    PROCEDURE SP_STATEMENT_MARK_INTEREST_APPLIED(
        p_statement_id IN NUMBER,
        o_cursor OUT SYS_REFCURSOR
    ) AS
    BEGIN
        UPDATE boa_statements SET interest_applied = 1 WHERE statement_id = p_statement_id;
        COMMIT;

        OPEN o_cursor FOR
        SELECT p_statement_id AS statement_id FROM dual;
    END SP_STATEMENT_MARK_INTEREST_APPLIED;

    -- 16. SP_CARD_RENEW: Kart Yenileme (Son Kullanma Tarihini Uzatma)
    PROCEDURE SP_CARD_RENEW(
        p_card_id IN NUMBER,
        p_new_expiry_date IN TIMESTAMP,
        p_user_id IN VARCHAR2,
        p_channel IN VARCHAR2,
        p_client_ip IN VARCHAR2,
        o_cursor OUT SYS_REFCURSOR
    ) AS
        v_old_expiry TIMESTAMP;
    BEGIN
        SELECT expiry_date INTO v_old_expiry FROM boa_cards WHERE card_id = p_card_id FOR UPDATE;

        UPDATE boa_cards SET expiry_date = p_new_expiry_date WHERE card_id = p_card_id;

        INSERT INTO boa_card_audit_log (audit_id, card_id, operation_type, old_value, new_value, reason, user_id, channel, client_ip)
        VALUES (SEQ_BOA_AUDIT.NEXTVAL, p_card_id, 'CARD_RENEWED', TO_CHAR(v_old_expiry), TO_CHAR(p_new_expiry_date), 'Son kullanma tarihi yaklaştığı için otomatik yenileme (EOD Batch)', p_user_id, p_channel, p_client_ip);

        COMMIT;

        OPEN o_cursor FOR
        SELECT c.card_id, c.card_number, c.card_holder_name, c.card_type, c.expiry_date, c.status, c.card_limit, c.balance, c.created_date,
               c.customer_id, c.bank_account_id, cu.national_id
        FROM boa_cards c
        JOIN boa_customers cu ON c.customer_id = cu.customer_id
        WHERE c.card_id = p_card_id;
    END SP_CARD_RENEW;

END PKG_BOA_CARD;
/

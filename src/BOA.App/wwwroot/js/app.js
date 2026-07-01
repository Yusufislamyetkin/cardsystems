/* =====================================================================================
   EMLAK KATILIM - BOA KART OPERASYONLARI PORTALI - FRONTEND KONTROL DOSYASI (JS)
   =====================================================================================
   Bu script, tarayıcı ekranlarındaki hareketleri kontrol eder, API uç noktalarına AJAX (fetch)
   istekleri atar ve arka plandaki WCF/SQL loglarını gerçek zamanlı olarak ekrana yansıtır.
*/

// Uygulama Durum Yönetimi (State)
const state = {
    cards: [],            // Yüklenen tüm kartlar
    selectedCardId: null, // Şu an seçili olan kartın ID'si
    selectedCard: null    // Seçili kartın detay nesnesi
};

// Tüm /api/cards/* çağrılarında sunucunun ValidateSecurityHeader/CheckRole kontrolünden geçebilmek
// için gönderilen kimlik doğrulama başlığı (WPF istemcisindeki mock JWT token ile aynı yapıdadır).
// Servis katmanında çoğu operasyon "BranchTeller" rolü gerektirir; yalnızca limit/durum güncelleme
// gibi yönetimsel işlemler "CardOperationsAdmin" ister — bu yüzden rol parametrik hale getirildi.
function apiHeaders(role = "TELLER") {
    return {
        "Content-Type": "application/json",
        "X-Security-Token": role === "ADMIN" ? "MOCK_JWT_ADMIN_TOKEN" : "MOCK_JWT_TELLER_TOKEN"
    };
}

// Sayfa Yüklendiğinde İlk Tetiklenecek Olaylar
document.addEventListener("DOMContentLoaded", () => {
    // 1. Kartları veritabanından çek ve listele
    fetchCards();

    // 2. Olay Dinleyicilerini (Event Listeners) Kaydet
    registerEventHandlers();

    // 3. WCF ve SQL Loglarını izlemek için arka planda anlık dinleme (Polling) başlat
    setInterval(pollLogs, 1500); // 1.5 saniyede bir logları çeker
});

// =====================================================================================
// API ÇAĞRILARI (FETCH / AJAX) KATMANI
// =====================================================================================

// YORDAM 1: Kartları Veritabanından Getirme
async function fetchCards() {
    const listContainer = document.getElementById("card-list");
    
    // Arama filtre kutularından değerleri alıyoruz
    const holderFilter = document.getElementById("filter-holder").value;
    const typeFilter = document.getElementById("filter-type").value;
    const statusFilter = document.getElementById("filter-status").value;

    // BOA GetCardListRequest WCF sözleşmesi veri yapısına uygun Request nesnesi
    const requestPayload = {
        CardHolderNameFilter: holderFilter || null,
        CardTypeFilter: typeFilter ? parseInt(typeFilter) : null,
        StatusFilter: statusFilter ? parseInt(statusFilter) : null,
        // RequestBase'den gelen denetim alanları
        Channel: "WEB_PORTAL",
        UserId: "YUSUF_BORA",
        BranchId: 999 // Merkez Şube
    };

    try {
        const response = await fetch("/api/cards/list", {
            method: "POST",
            headers: apiHeaders(),
            body: JSON.stringify(requestPayload)
        });

        const data = await response.json();

        if (data.isSuccess) {
            state.cards = data.cards;
            renderCardList();
            updateSummaryStats();
        } else {
            showNotification("Kartlar listelenirken hata oluştu: " + data.errorMessage, "danger");
        }
    } catch (error) {
        console.error("Kart listesi çekilemedi:", error);
    }
}

// YORDAM 2: Yeni Kart Oluşturma (Kart Tanımlama)
async function createCard(event) {
    event.preventDefault(); // Sayfanın yenilenmesini engeller

    const holder = document.getElementById("input-holder").value;
    const nationalId = document.getElementById("input-national-id").value;
    const type = parseInt(document.getElementById("select-card-type").value);
    const limit = parseFloat(document.getElementById("input-limit").value);
    const balance = parseFloat(document.getElementById("input-balance").value);
    const pin = document.getElementById("input-card-pin").value;

    // BOA CreateCardRequest WCF veri sözleşmesi nesnesi
    const requestPayload = {
        CardHolderName: holder,
        NationalId: nationalId,
        CardType: type,
        Limit: limit,
        InitialBalance: balance,
        Pin: pin,
        Channel: "WEB_PORTAL",
        UserId: "YUSUF_BORA",
        BranchId: 999
    };

    try {
        const response = await fetch("/api/cards/create", {
            method: "POST",
            headers: apiHeaders(),
            body: JSON.stringify(requestPayload)
        });

        const data = await response.json();

        if (data.isSuccess) {
            showNotification(data.resultMessage, "success");
            closeModal("modal-create-card");
            document.getElementById("form-create-card").reset();
            // Kart listesini yeniden yükle
            await fetchCards();
        } else {
            showNotification("Kart tanımlanamadı: " + data.errorMessage, "danger");
        }
    } catch (error) {
        showNotification("Servis bağlantı hatası!", "danger");
    }
}

// YORDAM 3: Limit Güncelleme
async function updateLimit(event) {
    event.preventDefault();

    const cardId = parseInt(document.getElementById("limit-card-id").value);
    const newLimit = parseFloat(document.getElementById("input-new-limit").value);

    // BOA UpdateCardLimitRequest veri sözleşmesi nesnesi
    const requestPayload = {
        CardId: cardId,
        NewLimit: newLimit,
        Channel: "WEB_PORTAL",
        UserId: "YUSUF_BORA"
    };

    try {
        const response = await fetch("/api/cards/update-limit", {
            method: "POST",
            headers: apiHeaders("ADMIN"),
            body: JSON.stringify(requestPayload)
        });

        const data = await response.json();

        if (data.isSuccess) {
            showNotification(data.resultMessage, "success");
            closeModal("modal-limit");
            
            // Seçili kart verisini güncelle ve arayüzü yenile
            state.selectedCard = data.updatedCard;
            selectCard(cardId);
            fetchCards(); // Listeyi de tazele
        } else {
            showNotification("Limit güncellenemedi: " + data.errorMessage, "danger");
        }
    } catch (error) {
        showNotification("Hata oluştu!", "danger");
    }
}

// YORDAM 4: Durum Değişikliği (Blokaj / İptal)
async function updateStatus(event) {
    event.preventDefault();

    const cardId = parseInt(document.getElementById("status-card-id").value);
    const newStatus = parseInt(document.getElementById("select-new-status").value);
    const reason = document.getElementById("input-status-reason").value;

    // BOA SetCardStatusRequest veri sözleşmesi nesnesi
    const requestPayload = {
        CardId: cardId,
        NewStatus: newStatus,
        Reason: reason,
        Channel: "WEB_PORTAL",
        UserId: "YUSUF_BORA"
    };

    try {
        const response = await fetch("/api/cards/set-status", {
            method: "POST",
            headers: apiHeaders("ADMIN"),
            body: JSON.stringify(requestPayload)
        });

        const data = await response.json();

        if (data.isSuccess) {
            showNotification(data.resultMessage, "success");
            closeModal("modal-status");
            
            state.selectedCard = data.updatedCard;
            selectCard(cardId);
            fetchCards();
        } else {
            showNotification("Durum değiştirilemedi: " + data.errorMessage, "danger");
        }
    } catch (error) {
        showNotification("Hata oluştu!", "danger");
    }
}

// YORDAM 5: Kart Hareketleri (Ekstre) Sorgulama
async function fetchTransactions(cardId) {
    const requestPayload = {
        CardId: cardId,
        Channel: "WEB_PORTAL",
        UserId: "YUSUF_BORA"
    };

    try {
        const response = await fetch("/api/cards/transactions", {
            method: "POST",
            headers: apiHeaders(),
            body: JSON.stringify(requestPayload)
        });

        const data = await response.json();

        if (data.isSuccess) {
            renderTransactions(data.transactions);
        } else {
            console.error("Hareketler yüklenemedi:", data.errorMessage);
        }
    } catch (error) {
        console.error("Bağlantı hatası:", error);
    }
}

// YORDAM 6: Kart İşlem Simülasyonu
async function simulateTransaction(event) {
    event.preventDefault();

    const cardId = parseInt(document.getElementById("trans-card-id").value);
    const type = parseInt(document.getElementById("select-trans-type").value);
    const amount = parseFloat(document.getElementById("input-trans-amount").value);
    const desc = document.getElementById("input-trans-desc").value;
    const pin = document.getElementById("input-trans-pin").value;

    // BOA CreateTransactionRequest veri sözleşmesi nesnesi
    const requestPayload = {
        CardId: cardId,
        TransactionType: type,
        Amount: amount,
        Description: desc,
        Pin: pin,
        Channel: "WEB_PORTAL",
        UserId: "YUSUF_BORA"
    };

    try {
        const response = await fetch("/api/cards/create-transaction", {
            method: "POST",
            headers: apiHeaders(),
            body: JSON.stringify(requestPayload)
        });

        const data = await response.json();

        if (data.isSuccess) {
            showNotification(data.resultMessage, "success");
            closeModal("modal-transaction");
            document.getElementById("form-create-transaction").reset();
            
            // Kart bakiyesini güncelle ve hareketleri yeniden çek
            state.selectedCard = data.updatedCard;
            selectCard(cardId);
            fetchCards();
        } else {
            showNotification("İşlem reddedildi: " + data.errorMessage, "danger");
        }
    } catch (error) {
        showNotification("Hata oluştu!", "danger");
    }
}

// YORDAM 7: Provizyon Alma (Authorize) — onaylanırsa kullanıcıya Capture/Void seçeneği sunar.
// Authorize→Capture→Void akışı: provizyon alındığında tutar yalnızca bloke edilir,
// yevmiye defterine hiçbir kayıt yazılmaz; kesinleşme ayrı bir Capture çağrısıyla olur.
async function authorizeTransaction(event) {
    event.preventDefault();

    const cardId = parseInt(document.getElementById("auth-card-id").value);
    const amount = parseFloat(document.getElementById("input-auth-amount").value);
    const pin = document.getElementById("input-auth-pin").value;
    const merchantId = document.getElementById("input-auth-merchant").value;
    const mcc = document.getElementById("input-auth-mcc").value;

    const requestPayload = {
        CardId: cardId,
        TransactionType: 1, // Purchase
        Amount: amount,
        Description: "Web Portal Provizyon Simülasyonu",
        Pin: pin,
        MerchantId: merchantId,
        Mcc: mcc,
        Channel: "WEB_PORTAL",
        UserId: "YUSUF_BORA"
    };

    try {
        const response = await fetch("/api/cards/authorize", {
            method: "POST",
            headers: apiHeaders(),
            body: JSON.stringify(requestPayload)
        });
        const data = await response.json();

        if (!data.isSuccess || !data.authorization) {
            showNotification("Provizyon hatası: " + data.errorMessage, "danger");
            return;
        }

        closeModal("modal-authorize");
        document.getElementById("form-authorize").reset();

        // AuthorizationStatus: 1=Authorized, 2=Captured, 3=Voided, 4=Declined
        if (data.authorization.status !== 1) {
            showNotification(`Provizyon reddedildi. Yanıt Kodu: ${data.authorization.responseCode}`, "danger");
            return;
        }

        showNotification(`Provizyon onaylandı! Kod: ${data.authorization.authorizationCode} (Provizyon No: ${data.authorization.authorizationId})`, "success");

        const wantsCapture = confirm(
            `Provizyon onaylandı (Kod: ${data.authorization.authorizationCode}).\n\n` +
            "TAMAM = Kesinleştir (Capture)\nİptal = Provizyonu iptal et (Void)"
        );

        if (wantsCapture) {
            const captureRes = await fetch("/api/cards/capture", {
                method: "POST",
                headers: apiHeaders(),
                body: JSON.stringify({ AuthorizationId: data.authorization.authorizationId, Channel: "WEB_PORTAL", UserId: "YUSUF_BORA" })
            });
            const captureData = await captureRes.json();
            if (captureData.isSuccess) {
                showNotification(captureData.resultMessage, "success");
                state.selectedCard = captureData.updatedCard;
                selectCard(cardId);
                fetchCards();
            } else {
                showNotification("Capture hatası: " + captureData.errorMessage, "danger");
            }
        } else {
            const voidRes = await fetch("/api/cards/void", {
                method: "POST",
                headers: apiHeaders(),
                body: JSON.stringify({ AuthorizationId: data.authorization.authorizationId, Reason: "Kullanıcı web portalından iptal etti", Channel: "WEB_PORTAL", UserId: "YUSUF_BORA" })
            });
            const voidData = await voidRes.json();
            showNotification(voidData.isSuccess ? voidData.resultMessage : voidData.errorMessage, voidData.isSuccess ? "success" : "danger");
        }
    } catch (error) {
        showNotification("Hata oluştu!", "danger");
    }
}

// =====================================================================================
// ARAYÜZ (DOM) YAZMA VE GÖRSELLEŞTİRME KATMANI
// =====================================================================================

// Kart Listesini HTML'e Basar
function renderCardList() {
    const listContainer = document.getElementById("card-list");
    listContainer.innerHTML = ""; // Mevcut listeyi boşalt

    if (state.cards.length === 0) {
        listContainer.innerHTML = `
            <div class="empty-state">
                <i class="fa-solid fa-folder-open" style="font-size: 2rem; color: rgba(255,255,255,0.05)"></i>
                <p>Aranan kriterlerde tanımlı kart bulunamadı.</p>
            </div>`;
        return;
    }

    state.cards.forEach(card => {
        const isSelected = card.cardId === state.selectedCardId ? "selected" : "";
        const typeBadge = card.cardType === 1 ? "Banka" : "Kredi";
        const statusText = card.status === 1 ? "Aktif" : card.status === 2 ? "Bloke" : "İptal";
        
        const cardItem = document.createElement("div");
        cardItem.className = `list-card-item ${isSelected}`;
        cardItem.onclick = () => selectCard(card.cardId);

        cardItem.innerHTML = `
            <div class="list-card-left">
                <div class="card-type-icon">
                    <i class="fa-solid ${card.cardType === 1 ? 'fa-wallet' : 'fa-credit-card'}"></i>
                </div>
                <div class="card-meta">
                    <h4>${maskCardNumberString(card.cardNumber)}</h4>
                    <p>${card.cardHolderName}</p>
                </div>
            </div>
            <div class="list-card-right">
                <div class="card-limit-val">₺${card.balance.toLocaleString('tr-TR', { minimumFractionDigits: 2 })}</div>
                <span class="badge type-${card.cardType}">${typeBadge}</span>
                <span class="badge status-${card.status}">${statusText}</span>
            </div>
        `;

        listContainer.appendChild(cardItem);
    });
}

// Bir Kart Seçildiğinde Detay Panelini Günceller
function selectCard(cardId) {
    state.selectedCardId = cardId;
    
    // Seçili kart verisini state'ten buluyoruz
    const card = state.cards.find(c => c.cardId === cardId) || state.selectedCard;
    if (!card) return;

    state.selectedCard = card;

    // Arayüz listesinde seçili stilini güncellemek için yeniden listeyi basıyoruz
    renderCardList();

    // Detay panelini görünür kılıyoruz
    document.getElementById("no-selection-msg").classList.add("hidden");
    document.getElementById("details-content").classList.remove("hidden");

    // Görsel plastik kart mockup verilerini güncelle
    const visualCard = document.getElementById("visual-card");
    const numEl = document.getElementById("visual-card-number");
    const holderEl = document.getElementById("visual-card-holder");
    const expiryEl = document.getElementById("visual-card-expiry");
    const brandEl = document.getElementById("visual-card-brand");

    numEl.innerText = maskCardNumberString(card.cardNumber);
    holderEl.innerText = card.cardHolderName;
    
    // Son kullanma tarihini MM/YY formatına çevir
    const expDate = new Date(card.expiryDate);
    const month = String(expDate.getMonth() + 1).padStart(2, '0');
    const year = String(expDate.getFullYear()).substring(2);
    expiryEl.innerText = `${month}/${year}`;

    // Kart türüne göre tasarımı değiştir (Banka Kartı: Yeşil, Kredi Kartı: Koyu Gri/Altın)
    if (card.cardType === 2) {
        visualCard.className = "plastic-card credit";
        brandEl.innerHTML = '<i class="fa-brands fa-cc-mastercard"></i>'; // Kredi kartı Mastercard
    } else {
        visualCard.className = "plastic-card";
        brandEl.innerHTML = '<i class="fa-brands fa-cc-visa"></i>'; // Banka kartı Visa
    }

    // Kart bloke veya iptalse plastik kartı grileştir
    if (card.status !== 1) {
        visualCard.classList.add("blocked");
    }

    // Finansal özet alanlarını doldur
    document.getElementById("detail-card-limit").innerText = `₺${card.cardLimit.toLocaleString('tr-TR', { minimumFractionDigits: 2 })}`;
    document.getElementById("detail-card-balance").innerText = `₺${card.balance.toLocaleString('tr-TR', { minimumFractionDigits: 2 })}`;
    
    const balanceLabel = document.getElementById("detail-balance-label");
    balanceLabel.innerText = card.cardType === 2 ? "Kullanılabilir Limit:" : "Hesap Bakiyesi:";

    // Kart tipi badges
    const typeEl = document.getElementById("detail-card-type");
    typeEl.innerText = card.cardType === 1 ? "Banka Kartı (Debit)" : "Kredi Kartı";
    typeEl.className = `badge type-${card.cardType}`;

    // Kart durumu badges
    const statusEl = document.getElementById("detail-card-status");
    const statusText = card.status === 1 ? "Aktif" : card.status === 2 ? "Bloke (Geçici Kapalı)" : "İptal (Kullanıma Kapalı)";
    statusEl.innerText = statusText;
    statusEl.className = `badge status-${card.status}`;

    // Operasyon ikonlarını duruma göre uyarla (Eğer kart iptal ise işlem yapmayı kapat vb.)
    const statusBtn = document.getElementById("btn-open-status-modal");
    const statusIcon = document.getElementById("action-status-icon");
    const statusTitle = document.getElementById("action-status-title");

    if (card.status === 2) { // Bloke ise
        statusIcon.className = "action-icon green";
        statusIcon.innerHTML = '<i class="fa-solid fa-lock-open"></i>';
        statusTitle.innerText = "Blokaj Kaldır";
    } else {
        statusIcon.className = "action-icon red";
        statusIcon.innerHTML = '<i class="fa-solid fa-lock"></i>';
        statusTitle.innerText = "Kartı Bloke Et";
    }

    // Hareket geçmişini servisten çek
    fetchTransactions(cardId);
}

// Kart Hareketlerini Çiz
function renderTransactions(transactions) {
    const transContainer = document.getElementById("transaction-list");
    transContainer.innerHTML = "";

    if (transactions.length === 0) {
        transContainer.innerHTML = '<p class="empty-state" style="font-size:0.75rem; padding:1rem;">Kart hareketi bulunmuyor.</p>';
        return;
    }

    transactions.forEach(t => {
        const isExpense = t.transactionType === 1 || t.transactionType === 2 || t.transactionType === 4;
        const sign = isExpense ? "-" : "+";
        const amountClass = isExpense ? "expense" : "income";
        
        const date = new Date(t.transactionDate);
        const dateStr = `${date.getDate()} ${getMonthName(date.getMonth())} ${date.getFullYear()} ${String(date.getHours()).padStart(2, '0')}:${String(date.getMinutes()).padStart(2, '0')}`;

        const item = document.createElement("div");
        item.className = `trans-item type-${t.transactionType}`;
        item.innerHTML = `
            <div class="trans-left">
                <h5>${t.description}</h5>
                <span>${dateStr}</span>
            </div>
            <div class="trans-right">
                <span class="trans-amount ${amountClass}">${sign}₺${t.amount.toLocaleString('tr-TR', { minimumFractionDigits: 2 })}</span>
                <span class="trans-ref">${t.referenceNumber}</span>
            </div>
        `;
        transContainer.appendChild(item);
    });
}

// İstatistik Kartlarını Güncelle
function updateSummaryStats() {
    const total = state.cards.length;
    const debit = state.cards.filter(c => c.cardType === 1).length;
    const credit = state.cards.filter(c => c.cardType === 2).length;
    const active = state.cards.filter(c => c.status === 1).length;

    document.getElementById("stat-total-cards").innerText = total;
    document.getElementById("stat-debit-cards").innerText = debit;
    document.getElementById("stat-credit-cards").innerText = credit;
    document.getElementById("stat-active-cards").innerText = active;
}

// =====================================================================================
// GERÇEK ZAMANLI LOG TRACER KATMANI (SOAP & SQL İZLEME)
// =====================================================================================

// Arka plandaki logları REST API'den sorgular
async function pollLogs() {
    try {
        // 1. WCF SOAP Loglarını Çek
        const wcfRes = await fetch("/api/tracer/wcf-logs");
        const wcfLogs = await wcfRes.json();
        renderLogPanel("wcf-log-output", wcfLogs, "WCF");

        // 2. SQL Stored Procedure Loglarını Çek
        const sqlRes = await fetch("/api/tracer/sql-logs");
        const sqlLogs = await sqlRes.json();
        renderLogPanel("sql-log-output", sqlLogs, "SQL");
    } catch (e) {
        console.error("Loglar çekilemedi:", e);
    }
}

// Log verilerini panel içerisine basıp otomatik en aşağı kaydırır
function renderLogPanel(elementId, logs, type) {
    const panel = document.getElementById(elementId);
    
    if (logs.length === 0) {
        panel.innerHTML = `<div class="log-placeholder">Bekleyen ${type} işlemi bulunmuyor...</div>`;
        return;
    }

    // Renklendirme ve biçimlendirme kuralları uygulayarak logları basıyoruz
    const formattedHtml = logs.map(log => {
        if (type === "WCF") {
            // XML etiketlerini renklendir
            let formatted = log
                .replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")
                .replace(/(&lt;\/?soap.*?\/?&gt;)/g, '<span style="color:#a78bfa">$1</span>') // SOAP Zarfı mor
                .replace(/(&lt;\/?web.*?\/?&gt;)/g, '<span style="color:#34d399">$1</span>')  // Metot etiketleri yeşil
                .replace(/(Operation: \w+)/, '<span style="color:#38bdf8; font-weight:bold">$1</span>'); // İşlem adı mavi
            return `<div style="margin-bottom: 8px; border-bottom: 1px dashed rgba(255,255,255,0.05); padding-bottom: 4px;">${formatted}</div>`;
        } else {
            // SQL Stored Procedure çağrılarını renklendir
            let formatted = log
                .replace(/(EXEC \w+)/g, '<span style="color:#fde047; font-weight:bold">$1</span>') // EXEC SP altın sarısı
                .replace(/(p_\w+)/g, '<span style="color:#fb923c">$1</span>')                     // Parametre isimleri turuncu
                .replace(/(Süre: \d+ ms)/g, '<span style="color:#60a5fa">$1</span>')               // Çalışma süresi mavi
                .replace(/(HATA: .*?\|)/g, '<span style="color:#f87171; font-weight:bold">$1</span>'); // Hatalar kırmızı
            return `<div style="margin-bottom: 6px; font-family:var(--font-code)">${formatted}</div>`;
        }
    }).join("");

    // Eğer yeni içerik eklendiyse scroll'u en aşağıya taşı
    const isAtBottom = panel.scrollHeight - panel.clientHeight <= panel.scrollTop + 50;
    panel.innerHTML = formattedHtml;
    
    if (isAtBottom || panel.scrollTop === 0) {
        panel.scrollTop = panel.scrollHeight;
    }
}

// Gün Sonu (EOD) Batch Sürecini Tetikleme — kredi kartları için ekstre kesimi, gecikme faizi,
// otomatik blokaj ve kart yenileme işlemlerini çalıştırır. "CardOperationsAdmin" rolü gerektirir.
async function runEodBatch() {
    const confirmed = confirm(
        "Gün sonu (EOD) batch süreci çalıştırılacak:\n" +
        "• Borcu olan kredi kartlarına yeni ekstre kesilecek\n" +
        "• Vadesi geçmiş ekstrelere gecikme faizi işlenecek\n" +
        "• 30 günü aşan gecikmelerde kart otomatik bloke edilecek\n" +
        "• Son kullanma tarihi yaklaşan kartlar otomatik yenilenecek\n\n" +
        "Devam edilsin mi?"
    );
    if (!confirmed) return;

    try {
        const response = await fetch("/api/cards/run-eod-batch", {
            method: "POST",
            headers: apiHeaders("ADMIN"),
            body: JSON.stringify({ Channel: "WEB_PORTAL", UserId: "YUSUF_BORA" })
        });
        const data = await response.json();

        if (data.isSuccess) {
            showNotification(data.resultMessage, "success");
            fetchCards();
        } else {
            showNotification("Batch hatası: " + data.errorMessage, "danger");
        }
    } catch (error) {
        showNotification("Hata oluştu!", "danger");
    }
}

// Tracer Loglarını Temizleme API Çağrısı
async function clearTracerLogs() {
    try {
        await fetch("/api/tracer/clear", { method: "POST" });
        pollLogs(); // Anlık temizliği ekrana yansıt
        showNotification("İzleyici günlükleri temizlendi.", "success");
    } catch (e) {
        console.error(e);
    }
}

// =====================================================================================
// EVENT HANDLERS (OLAY DİNLEYİCİLERİ) VE YARDIMCILAR
// =====================================================================================

function registerEventHandlers() {
    // Arama filtre kutuları değiştiğinde listeyi otomatik yenile
    document.getElementById("filter-holder").addEventListener("input", fetchCards);
    document.getElementById("filter-type").addEventListener("change", fetchCards);
    document.getElementById("filter-status").addEventListener("change", fetchCards);

    // Form gönderim olayları
    document.getElementById("form-create-card").addEventListener("submit", createCard);
    document.getElementById("form-update-limit").addEventListener("submit", updateLimit);
    document.getElementById("form-update-status").addEventListener("submit", updateStatus);
    document.getElementById("form-create-transaction").addEventListener("submit", simulateTransaction);
    document.getElementById("form-authorize").addEventListener("submit", authorizeTransaction);

    // Log temizleme butonu
    document.getElementById("btn-clear-logs").addEventListener("click", clearTracerLogs);
    document.getElementById("btn-run-eod-batch").addEventListener("click", runEodBatch);

    // Modal açıcı butonlar
    document.getElementById("btn-open-create-modal").addEventListener("click", () => openModal("modal-create-card"));
    
    document.getElementById("btn-open-limit-modal").addEventListener("click", () => {
        if (!state.selectedCard) return;
        document.getElementById("limit-card-id").value = state.selectedCard.cardId;
        document.getElementById("limit-card-holder").innerText = state.selectedCard.cardHolderName;
        document.getElementById("limit-card-current").innerText = `₺${state.selectedCard.cardLimit.toLocaleString('tr-TR', { minimumFractionDigits: 2 })}`;
        document.getElementById("input-new-limit").value = state.selectedCard.cardLimit;
        openModal("modal-limit");
    });

    document.getElementById("btn-open-status-modal").addEventListener("click", () => {
        if (!state.selectedCard) return;
        document.getElementById("status-card-id").value = state.selectedCard.cardId;
        document.getElementById("status-card-number").innerText = maskCardNumberString(state.selectedCard.cardNumber);
        document.getElementById("select-new-status").value = state.selectedCard.status;
        document.getElementById("input-status-reason").value = "";
        openModal("modal-status");
    });

    document.getElementById("btn-open-trans-modal").addEventListener("click", () => {
        if (!state.selectedCard) return;
        document.getElementById("trans-card-id").value = state.selectedCard.cardId;
        document.getElementById("trans-card-holder").innerText = `${state.selectedCard.cardHolderName} (${maskCardNumberString(state.selectedCard.cardNumber)})`;
        document.getElementById("input-trans-amount").value = 100;
        document.getElementById("input-trans-desc").value = "";
        openModal("modal-transaction");
    });

    document.getElementById("btn-open-authorize-modal").addEventListener("click", () => {
        if (!state.selectedCard) return;
        document.getElementById("auth-card-id").value = state.selectedCard.cardId;
        document.getElementById("auth-card-holder").innerText = `${state.selectedCard.cardHolderName} (${maskCardNumberString(state.selectedCard.cardNumber)})`;
        document.getElementById("input-auth-amount").value = 150;
        document.getElementById("input-auth-pin").value = "1234";
        openModal("modal-authorize");
    });

    // Kapatma butonları ve modal dışına tıklama ile kapatma
    document.querySelectorAll(".modal-close, [data-modal]").forEach(el => {
        el.addEventListener("click", (e) => {
            const modalId = el.getAttribute("data-modal") || el.closest(".modal").id;
            closeModal(modalId);
        });
    });

    // Yeni Kart modalında kart türü değiştikçe limit/bakiye alanlarını göster/gizle
    // Debit kartlarda kredi limiti yoktur (yani 0'dır), Kredi kartlarında hesap bakiyesi limit ile başlar.
    document.getElementById("select-card-type").addEventListener("change", (e) => {
        const val = e.target.value;
        const limitGroup = document.getElementById("limit-group");
        const balanceGroup = document.getElementById("balance-group");
        
        if (val === "1") { // Debit
            limitGroup.style.display = "none";
            balanceGroup.style.display = "block";
            document.getElementById("input-limit").value = 0;
        } else { // Credit
            limitGroup.style.display = "block";
            balanceGroup.style.display = "none";
            document.getElementById("input-balance").value = 0;
        }
    });
    // İlk tetikleme
    document.getElementById("select-card-type").dispatchEvent(new Event("change"));
}

// Modal Açma / Kapatma Yardımcıları
function openModal(id) {
    document.getElementById(id).classList.add("active");
}
function closeModal(id) {
    document.getElementById(id).classList.remove("active");
}

// Bildirim Mesajı Çıkarma
function showNotification(message, type = "success") {
    // Ekranda yüzen bildirim penceresi oluşturur
    const toast = document.createElement("div");
    toast.className = `toast toast-${type}`;
    toast.style.position = "fixed";
    toast.style.top = "20px";
    toast.style.right = "20px";
    toast.style.background = type === "success" ? "#10b981" : "#ef4444";
    toast.style.color = "#fff";
    toast.style.padding = "0.8rem 1.5rem";
    toast.style.borderRadius = "8px";
    toast.style.boxShadow = "0 10px 15px -3px rgba(0, 0, 0, 0.3)";
    toast.style.zIndex = "9999";
    toast.style.fontSize = "0.85rem";
    toast.style.fontWeight = "500";
    toast.style.display = "flex";
    toast.style.alignItems = "center";
    toast.style.gap = "0.5rem";
    
    const icon = type === "success" ? "fa-circle-check" : "fa-circle-exclamation";
    toast.innerHTML = `<i class="fa-solid ${icon}"></i> <span>${message}</span>`;
    
    document.body.appendChild(toast);
    
    // 3.5 saniye sonra yok et
    setTimeout(() => {
        toast.style.opacity = "0";
        toast.style.transition = "opacity 0.5s";
        setTimeout(() => toast.remove(), 500);
    }, 3500);
}

// Maskeleme Fonksiyonu (Kart numaralarını gizler)
function maskCardNumberString(cardNo) {
    if (!cardNo || cardNo.length < 16) return cardNo;
    return `${cardNo.substring(0, 4)} ${cardNo.substring(4, 6)}** **** ${cardNo.substring(12, 16)}`;
}

// Ay İsmi Yardımcısı
function getMonthName(monthIdx) {
    const months = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
    return months[monthIdx];
}

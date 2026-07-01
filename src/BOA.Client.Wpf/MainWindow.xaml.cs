using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.ServiceModel;
using System.ServiceModel.Channels; // MessageHeader için
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;

namespace BOA.Client.Wpf;

/// <summary>
/// WPF Arayüzünün (Dashboard) arkasındaki olayları ve WCF SOAP kanallarını yöneten sınıftır.
/// </summary>
public partial class MainWindow : Window
{
    private ChannelFactory<ICardServiceClient> _wcfFactory = null!;
    private ICardServiceClient _wcfClient = null!;
    private CardDto? _selectedCard;

    // Log izleme API'sine istek atmak için kullanılan istemci
    private readonly HttpClient _httpClient = new HttpClient();
    
    // Canlı log takibi için zamanlayıcı (Timer)
    private readonly DispatcherTimer _logTimer = new DispatcherTimer();

    public MainWindow()
    {
        InitializeComponent();

        // 1. Log Zamanlayıcı Yapılandırması (Her 1 saniyede logları çek)
        _logTimer.Interval = TimeSpan.FromSeconds(1);
        _logTimer.Tick += LogTimer_Tick;
    }

    /// <summary>
    /// Pencere kaynakları hazır olduğunda WCF kanalını açar ve ilk yüklemeyi yapar.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        try
        {
            // A. WCF SOAP İletişim Kanallarını HTTPS Uç Noktası ile Güvenli (Transport Security) Olarak Yapılandır
            var binding = new BasicHttpBinding(BasicHttpSecurityMode.Transport);
            
            // Performans & Sıkılaştırma parametreleri (DoS önleme)
            binding.MaxReceivedMessageSize = 1048576; // 1MB XML sınırı
            binding.ReceiveTimeout = TimeSpan.FromSeconds(15);
            binding.SendTimeout = TimeSpan.FromSeconds(15);
            binding.OpenTimeout = TimeSpan.FromSeconds(15);
            binding.CloseTimeout = TimeSpan.FromSeconds(15);

            var endpoint = new EndpointAddress("https://localhost:5001/CardService.svc");

            _wcfFactory = new ChannelFactory<ICardServiceClient>(binding, endpoint);
            _wcfClient = _wcfFactory.CreateChannel();

            // B. Kayıtlı kartları listele
            LoadCards();

            // C. Log izleme zamanlayıcısını başlat
            _logTimer.Start();
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("c:\\Users\\Yusuf\\Desktop\\BOA\\wpf_error.txt", "OnSourceInitialized Exception: " + ex.ToString());
            MessageBox.Show($"WCF Sunucusuna bağlanılamadı. Lütfen sunucunun (BOA.App) çalıştığından emin olun.\nHata: {ex.Message}", 
                "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // =====================================================================================
    // GÜVENLİ KURUMSAL WCF YARDIMCI METOTLARI
    // =====================================================================================

    /// <summary>
    /// Tüm WCF SOAP çağrılarını sarmalayarak güvenlik başlığı enjekte eden ve 
    /// sunucudan dönen SOAP Fault'ları (BankingFault) yakalayan generic metottur.
    /// </summary>
    private TResult CallWcf<TResult>(Func<TResult> wcfAction)
    {
        // Sertifika doğrulaması: sadece bilinen localhost geliştirme sertifikası (zincir hatası dışında
        // her şey doğrulanmış olmalı) kabul edilir; farklı host veya başka hata türlerinde reddedilir.
        System.Net.ServicePointManager.ServerCertificateValidationCallback = (sender, cert, chain, errors) =>
        {
            if (errors == System.Net.Security.SslPolicyErrors.None) return true;

            if (errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors &&
                cert is System.Security.Cryptography.X509Certificates.X509Certificate2 x509 &&
                string.Equals(x509.GetNameInfo(System.Security.Cryptography.X509Certificates.X509NameType.DnsName, false), "localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        };

        // ComboBox'tan seçilen role göre yetkilendirme belirteci (SecurityToken) al
        string token = "MOCK_JWT_TELLER_TOKEN";
        if (CmbUserRole != null && CmbUserRole.SelectedItem is ComboBoxItem selectedItem)
        {
            token = selectedItem.Tag?.ToString() ?? "MOCK_JWT_TELLER_TOKEN";
        }

        try
        {
            using (new OperationContextScope((IContextChannel)_wcfClient))
            {
                // WS-Security standardında custom SOAP Header ekleme
                var header = MessageHeader.CreateHeader("SecurityToken", "http://emlakkatilim.com.tr/security", token);
                OperationContext.Current.OutgoingMessageHeaders.Add(header);

                // Asıl WCF çağrısını çalıştır
                return wcfAction();
            }
        }
        catch (FaultException<BankingFault> fex)
        {
            // Kurumsal SOAP Hata İletimi (Fault Contract) yakalandı
            var fault = fex.Detail;
            string severityIcon = fault.Severity == "Critical" ? "🚫" : "⚠️";
            MessageBox.Show(
                $"{severityIcon} Kurumsal BOA Hata Bildirimi\n\n" +
                $"Hata Kodu: {fault.ErrorCode}\n" +
                $"Hata Detayı: {fault.ErrorMessage}\n" +
                $"Önem Derecesi: {fault.Severity}\n" +
                $"Zaman Damgası: {fault.Timestamp.ToLocalTime()}",
                "Emlak Katılım BOA Hata Yönetimi", 
                MessageBoxButton.OK, 
                MessageBoxImage.Warning);
            
            throw; // Arayüzün hatalı güncellenmesini engellemek için yeniden fırlatıyoruz
        }
        catch (CommunicationException cex)
        {
            MessageBox.Show($"WCF SOAP Haberleşme Hatası: Sunucuyla şifreli HTTPS kanalı kurulamadı.\nDetay: {cex.Message}", 
                "Haberleşme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Beklenmeyen Bir Hata Oluştu:\n{ex.Message}", 
                "Sistem Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
            throw;
        }
    }

    private void CallWcfAction(Action wcfAction)
    {
        CallWcf<object?>(() => { wcfAction(); return null; });
    }

    // =====================================================================================
    // KART LİSTELEME VE DETAY GÖRÜNTÜLEME OLAYLARI
    // =====================================================================================

    /// <summary>
    /// WCF GetCardList metodunu SOAP üzerinden çağırır ve ListBox kontrolüne bağlar.
    /// </summary>
    private void LoadCards(string? holderFilter = null, CardType? typeFilter = null)
    {
        if (_wcfClient == null) return;

        try
        {
            var request = new GetCardListRequest
            {
                CardHolderNameFilter = holderFilter,
                CardTypeFilter = typeFilter,
                Channel = "WPF_DESKTOP",
                UserId = "DESKTOP_ADMIN"
            };

            var response = CallWcf(() => _wcfClient.GetCardList(request));

            if (response.IsSuccess)
            {
                LstCards.ItemsSource = response.Cards;

                // Eğer listede daha önce seçili bir kart varsa, listede yeniden bulup seçili kıl
                if (_selectedCard != null)
                {
                    var reselected = response.Cards.FirstOrDefault(c => c.CardId == _selectedCard.CardId);
                    if (reselected != null)
                    {
                        LstCards.SelectedItem = reselected;
                    }
                }
            }
            else
            {
                MessageBox.Show(response.ErrorMessage, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.IO.File.WriteAllText("c:\\Users\\Yusuf\\Desktop\\BOA\\wpf_error.txt", "LoadCards Exception: " + ex.ToString());
        }
    }

    /// <summary>
    /// ListBox üzerinden bir kart seçildiğinde detayları doldurur.
    /// </summary>
    private void LstCards_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LstCards.SelectedItem is not CardDto card)
        {
            _selectedCard = null;
            PanelNoSelection.Visibility = Visibility.Visible;
            PanelSelection.Visibility = Visibility.Collapsed;
            return;
        }

        _selectedCard = card;
        PanelNoSelection.Visibility = Visibility.Collapsed;
        PanelSelection.Visibility = Visibility.Visible;

        // A. Plastik Kart Görsel Detaylarını Güncelle (PAN Maskeli)
        TxtVisualCardNo.Text = card.MaskedCardNumber;
        TxtVisualHolder.Text = card.CardHolderName;
        TxtVisualExpiry.Text = $"{card.ExpiryDate:MM/yy}";

        // Kart türüne göre renk ve marka simülasyonu
        if (card.CardType == CardType.Credit)
        {
            TxtVisualBrand.Text = "MASTERCARD";
            VisualPlasticCard.Background = new LinearGradientBrush(
                Color.FromRgb(0x1f, 0x1f, 0x21), 
                Color.FromRgb(0x3a, 0x3a, 0x3d), 
                45.0);
        }
        else
        {
            TxtVisualBrand.Text = "VISA";
            VisualPlasticCard.Background = new LinearGradientBrush(
                Color.FromRgb(0x0f, 0x1c, 0x18), 
                Color.FromRgb(0x00, 0x5a, 0x3c), 
                45.0);
        }

        VisualPlasticCard.Opacity = card.Status == CardStatus.Active ? 1.0 : 0.45;

        // B. Finansal Metin Bilgilerini Güncelle
        TxtDetailLimit.Text = $"₺{card.CardLimit:N2}";
        TxtDetailBalance.Text = $"₺{card.Balance:N2}";
        TxtDetailBalanceLabel.Text = card.CardType == CardType.Credit ? "Usable Bakiye (Limit)" : "GL Hesap Bakiyesi";
        
        // Defter-i Kebir Hesap No Entegrasyonu
        TxtDetailGlAccount.Text = card.GlAccountNumber;

        BtnBlockCard.Content = card.Status == CardStatus.Blocked ? "🔓 Blokaj Kaldır" : "🔒 Bloke Et";
        BtnBlockCard.Background = card.Status == CardStatus.Blocked ? new SolidColorBrush(Color.FromRgb(0x16, 0xa3, 0x4a)) : new SolidColorBrush(Color.FromRgb(0xdc, 0x26, 0x26));

        // C. Kart Hareketlerini (Ekstre) WCF SOAP ile Yükle
        LoadTransactions(card.CardId);
    }

    /// <summary>
    /// Karta ait hareketleri SOAP üzerinden çeker ve DataGrid kontrolüne bağlar.
    /// </summary>
    private void LoadTransactions(int cardId)
    {
        try
        {
            var request = new GetCardTransactionsRequest { CardId = cardId, Channel = "WPF_DESKTOP" };
            var response = CallWcf(() => _wcfClient.GetCardTransactions(request));

            if (response.IsSuccess)
            {
                DgTransactions.ItemsSource = response.Transactions;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Hareketler yüklenirken hata oluştu: " + ex.Message);
        }
    }

    // =====================================================================================
    // FORM ETKİLEŞİM VE GÖSTERME/GİZLEME OLAYLARI
    // =====================================================================================

    private void HideAllSubForms()
    {
        BorderLimitForm.Visibility = Visibility.Collapsed;
        BorderStatusForm.Visibility = Visibility.Collapsed;
        BorderSimulateForm.Visibility = Visibility.Collapsed;
        BorderNewCardForm.Visibility = Visibility.Collapsed;
        BorderVerifyPinForm.Visibility = Visibility.Collapsed;
        BorderAuthorizeForm.Visibility = Visibility.Collapsed;
    }

    private void BtnNewCard_Click(object sender, RoutedEventArgs e)
    {
        HideAllSubForms();
        BorderNewCardForm.Visibility = Visibility.Visible;
        TxtNewCardHolder.Text = "";
        TxtNewCardNationalId.Text = "";
        TxtNewCardBalance.Text = "1000";
        TxtNewCardLimit.Text = "0";
        TxtNewCardPin.Password = "1234";
    }

    private void BtnLimitUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;
        HideAllSubForms();
        BorderLimitForm.Visibility = Visibility.Visible;
        TxtNewLimitVal.Text = _selectedCard.CardLimit.ToString("F0");
    }

    private void BtnBlock_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;
        HideAllSubForms();
        BorderStatusForm.Visibility = Visibility.Visible;

        // Kart bloke ise aktifleştirmeye, aktif ise bloke etmeye ön-hazırlık yap
        if (_selectedCard.Status == CardStatus.Blocked)
        {
            CmbNewStatus.SelectedIndex = 0; // Aktif
            TxtStatusReason.Text = "Blokaj Kaldırma Talebi";
        }
        else
        {
            CmbNewStatus.SelectedIndex = 1; // Bloke
            TxtStatusReason.Text = "Müşteri Kayıp Çalıntı İhbarı";
        }
    }

    private void BtnSimulateTrans_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;
        HideAllSubForms();
        BorderSimulateForm.Visibility = Visibility.Visible;
        TxtTransAmount.Text = "150";
        TxtTransPin.Password = "1234";
        TxtTransDesc.Text = "POS Alışveriş Simülasyonu";
        CmbTransType.SelectedIndex = 0; // Alışveriş
    }

    private void BtnVerifyPin_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;
        HideAllSubForms();
        BorderVerifyPinForm.Visibility = Visibility.Visible;
        TxtVerifyPinVal.Password = "1234";
    }

    private void BtnAuthorize_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;
        HideAllSubForms();
        BorderAuthorizeForm.Visibility = Visibility.Visible;
        TxtAuthAmount.Text = "150";
        TxtAuthPin.Password = "1234";
        TxtAuthMerchant.Text = "MIGROS-1234";
        TxtAuthMcc.Text = "5411";
    }

    private void BtnCancelSubForm_Click(object sender, RoutedEventArgs e)
    {
        HideAllSubForms();
    }

    private void CmbUserRole_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FilterAndReload();
    }

    // =====================================================================================
    // WCF SERVİS TETİKLEME FORMLARI (SOAP FAULT VE PIN YÖNETİMLİ)
    // =====================================================================================

    // 1. Yeni Kart Kaydetme (CreateCard SOAP - Şifre parametresiyle)
    private void BtnSubmitNewCard_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string holder = TxtNewCardHolder.Text;
            var typeItem = (ComboBoxItem)CmbNewCardType.SelectedItem;
            CardType cardType = typeItem.Tag.ToString() == "2" ? CardType.Credit : CardType.Debit;
            
            decimal.TryParse(TxtNewCardLimit.Text, out decimal limit);
            decimal.TryParse(TxtNewCardBalance.Text, out decimal balance);

            var request = new CreateCardRequest
            {
                CardHolderName = holder.ToUpperInvariant(),
                NationalId = TxtNewCardNationalId.Text,
                CardType = cardType,
                Limit = limit,
                InitialBalance = balance,
                Pin = TxtNewCardPin.Password, // HSM PIN Block üretilecek ilk şifre
                Channel = "WPF_DESKTOP",
                UserId = "DESKTOP_ADMIN"
            };

            var response = CallWcf(() => _wcfClient.CreateCard(request));

            if (response.IsSuccess)
            {
                HideAllSubForms();
                _selectedCard = response.CreatedCard;
                LoadCards();
                MessageBox.Show(response.ResultMessage, "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(response.ErrorMessage, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception)
        {
        }
    }

    // 2. Limit Güncelleme (UpdateCardLimit SOAP)
    private void BtnSubmitLimit_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;

        try
        {
            decimal.TryParse(TxtNewLimitVal.Text, out decimal newLimit);

            var request = new UpdateCardLimitRequest
            {
                CardId = _selectedCard.CardId,
                NewLimit = newLimit,
                Channel = "WPF_DESKTOP",
                UserId = "DESKTOP_ADMIN"
            };

            var response = CallWcf(() => _wcfClient.UpdateCardLimit(request));

            if (response.IsSuccess)
            {
                HideAllSubForms();
                _selectedCard = response.UpdatedCard;
                LoadCards();
            }
            else
            {
                MessageBox.Show(response.ErrorMessage, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception)
        {
        }
    }

    // 3. Durum Değiştirme (SetCardStatus SOAP)
    private void BtnSubmitStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;

        try
        {
            var statusItem = (ComboBoxItem)CmbNewStatus.SelectedItem;
            CardStatus newStatus = (CardStatus)int.Parse(statusItem.Tag.ToString()!);
            string reason = TxtStatusReason.Text;

            var request = new SetCardStatusRequest
            {
                CardId = _selectedCard.CardId,
                NewStatus = newStatus,
                Reason = reason,
                Channel = "WPF_DESKTOP",
                UserId = "DESKTOP_ADMIN"
            };

            var response = CallWcf(() => _wcfClient.SetCardStatus(request));

            if (response.IsSuccess)
            {
                HideAllSubForms();
                _selectedCard = response.UpdatedCard;
                LoadCards();
            }
            else
            {
                MessageBox.Show(response.ErrorMessage, "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception)
        {
        }
    }

    // 4. İşlem Simüle Etme (CreateTransaction SOAP - Şifre doğrulamalı)
    private void BtnSubmitTrans_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;

        try
        {
            var typeItem = (ComboBoxItem)CmbTransType.SelectedItem;
            TransactionType transType = (TransactionType)int.Parse(typeItem.Tag.ToString()!);
            decimal.TryParse(TxtTransAmount.Text, out decimal amount);
            string desc = TxtTransDesc.Text;

            var request = new CreateTransactionRequest
            {
                CardId = _selectedCard.CardId,
                TransactionType = transType,
                Amount = amount,
                Description = desc,
                Pin = TxtTransPin.Password, // Harcama/Çekimde HSM ile doğrulanacak PIN
                Channel = "WPF_DESKTOP",
                UserId = "DESKTOP_ADMIN"
            };

            var response = CallWcf(() => _wcfClient.CreateTransaction(request));

            if (response.IsSuccess)
            {
                HideAllSubForms();
                _selectedCard = response.UpdatedCard;
                LoadCards();
            }
            else
            {
                MessageBox.Show(response.ErrorMessage, "İşlem Reddedildi", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception)
        {
        }
    }

    // 5. Şifre Doğrulama (VerifyPin SOAP - HSM PIN Block testi)
    private void BtnSubmitVerifyPin_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;

        try
        {
            var request = new VerifyPinRequest
            {
                CardId = _selectedCard.CardId,
                Pin = TxtVerifyPinVal.Password,
                Channel = "WPF_DESKTOP",
                UserId = "DESKTOP_ADMIN"
            };

            var response = CallWcf(() => _wcfClient.VerifyPin(request));

            if (response.IsSuccess)
            {
                HideAllSubForms();
                if (response.IsPinValid)
                {
                    MessageBox.Show("✅ HSM Şifre Doğrulama Başarılı!\nGirdiğiniz 4 haneli şifre, kartın ISO 9564 PIN Block (Format 0) kaydıyla tam eşleşti.", 
                        "HSM Şifre Doğrulandı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("❌ HSM Şifre Doğrulama Başarısız!\nGirdiğiniz şifre kartın veritabanındaki PIN block kaydıyla uyuşmamaktadır.",
                        "Hatalı Şifre", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    // 6. Provizyon Alma (AuthorizeTransaction SOAP - Authorize→Capture→Void akışının ilk adımı)
    private void BtnSubmitAuthorize_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedCard == null) return;

        try
        {
            decimal.TryParse(TxtAuthAmount.Text, out decimal amount);

            var request = new AuthorizeTransactionRequest
            {
                CardId = _selectedCard.CardId,
                TransactionType = TransactionType.Purchase,
                Amount = amount,
                Description = "WPF Provizyon Simülasyonu",
                Pin = TxtAuthPin.Password,
                MerchantId = TxtAuthMerchant.Text,
                Mcc = TxtAuthMcc.Text,
                Channel = "WPF_DESKTOP",
                UserId = "DESKTOP_ADMIN"
            };

            var response = CallWcf(() => _wcfClient.AuthorizeTransaction(request));

            if (!response.IsSuccess || response.Authorization == null)
            {
                MessageBox.Show(response.ErrorMessage, "Provizyon Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            HideAllSubForms();

            if (response.Authorization.Status != AuthorizationStatus.Authorized)
            {
                MessageBox.Show(
                    $"❌ Provizyon Reddedildi!\nYanıt Kodu: {(int)response.Authorization.ResponseCode:D2} ({response.Authorization.ResponseCode})",
                    "Provizyon Reddedildi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var choice = MessageBox.Show(
                $"✅ Provizyon Onaylandı!\nProvizyon Kodu: {response.Authorization.AuthorizationCode}\nProvizyon No: {response.Authorization.AuthorizationId}\n\n" +
                "Şimdi kesinleştirmek (Capture) ister misiniz?\nEvet = Capture, Hayır = Void (iptal), İptal = Bekleyen provizyon olarak bırak.",
                "Provizyon Onaylandı", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (choice == MessageBoxResult.Yes)
            {
                var captureResponse = CallWcf(() => _wcfClient.CaptureAuthorization(new CaptureAuthorizationRequest
                {
                    AuthorizationId = response.Authorization.AuthorizationId,
                    Channel = "WPF_DESKTOP",
                    UserId = "DESKTOP_ADMIN"
                }));

                if (captureResponse.IsSuccess)
                {
                    _selectedCard = captureResponse.UpdatedCard;
                    LoadCards();
                    MessageBox.Show(captureResponse.ResultMessage, "Provizyon Kesinleştirildi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(captureResponse.ErrorMessage, "Capture Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else if (choice == MessageBoxResult.No)
            {
                var voidResponse = CallWcf(() => _wcfClient.VoidAuthorization(new VoidAuthorizationRequest
                {
                    AuthorizationId = response.Authorization.AuthorizationId,
                    Reason = "Kullanıcı WPF ekranından iptal etti",
                    Channel = "WPF_DESKTOP",
                    UserId = "DESKTOP_ADMIN"
                }));

                MessageBox.Show(voidResponse.IsSuccess ? voidResponse.ResultMessage : voidResponse.ErrorMessage,
                    "Provizyon İptal Edildi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception)
        {
        }
    }

    // =====================================================================================
    // CANLI LOG İZLEYİCİ VE YARDIMCI OLAYLAR
    // =====================================================================================

    private async void LogTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var wcfLogs = await _httpClient.GetFromJsonAsync<List<string>>("http://localhost:5000/api/tracer/wcf-logs");
            if (wcfLogs != null)
            {
                string text = string.Join(Environment.NewLine + Environment.NewLine, wcfLogs);
                if (TxtWcfLogs.Text != text)
                {
                    TxtWcfLogs.Text = text;
                    TxtWcfLogs.ScrollToEnd();
                }
            }

            var sqlLogs = await _httpClient.GetFromJsonAsync<List<string>>("http://localhost:5000/api/tracer/sql-logs");
            if (sqlLogs != null)
            {
                string text = string.Join(Environment.NewLine, sqlLogs);
                if (TxtSqlLogs.Text != text)
                {
                    TxtSqlLogs.Text = text;
                    TxtSqlLogs.ScrollToEnd();
                }
            }
        }
        catch
        {
        }
    }

    private void TxtSearchHolder_TextChanged(object sender, TextChangedEventArgs e)
    {
        FilterAndReload();
    }

    private void CmbSearchType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        FilterAndReload();
    }

    private void FilterAndReload()
    {
        if (CmbSearchType == null || TxtSearchHolder == null) return;

        string holder = TxtSearchHolder.Text;
        CardType? type = null;
        if (CmbSearchType.SelectedIndex == 1) type = CardType.Debit;
        else if (CmbSearchType.SelectedIndex == 2) type = CardType.Credit;

        LoadCards(string.IsNullOrWhiteSpace(holder) ? null : holder, type);
    }

    private void CmbNewCardType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbNewCardType == null || TxtNewCardLimit == null || TxtNewCardBalance == null) return;

        var item = (ComboBoxItem)CmbNewCardType.SelectedItem;
        if (item.Tag.ToString() == "1") // Banka
        {
            LblNewCardLimit.Visibility = Visibility.Collapsed;
            TxtNewCardLimit.Visibility = Visibility.Collapsed;
            TxtNewCardLimit.Text = "0";

            LblNewCardBalance.Visibility = Visibility.Visible;
            TxtNewCardBalance.Visibility = Visibility.Visible;
        }
        else // Kredi
        {
            LblNewCardLimit.Visibility = Visibility.Visible;
            TxtNewCardLimit.Visibility = Visibility.Visible;

            LblNewCardBalance.Visibility = Visibility.Collapsed;
            TxtNewCardBalance.Visibility = Visibility.Collapsed;
            TxtNewCardBalance.Text = "0";
        }
    }

    // Gün Sonu (EOD) Batch Süreci: Kredi kartları için ekstre kesimi, gecikme faizi, otomatik blokaj
    // ve kart yenileme işlemlerini tetikler. "CardOperationsAdmin" rolü gerektirir.
    private void BtnRunEodBatch_Click(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "Gün sonu (EOD) batch süreci çalıştırılacak:\n" +
            "• Borcu olan kredi kartlarına yeni ekstre kesilecek\n" +
            "• Vadesi geçmiş ekstrelere gecikme faizi işlenecek\n" +
            "• 30 günü aşan gecikmelerde kart otomatik bloke edilecek\n" +
            "• Son kullanma tarihi yaklaşan kartlar otomatik yenilenecek\n\n" +
            "Devam edilsin mi?",
            "Gün Sonu Batch Onayı", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        try
        {
            var response = CallWcf(() => _wcfClient.RunEodBatch(new RunEodBatchRequest { Channel = "WPF_DESKTOP", UserId = "DESKTOP_ADMIN" }));

            if (response.IsSuccess)
            {
                MessageBox.Show(response.ResultMessage, "Gün Sonu Batch Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadCards();
            }
            else
            {
                MessageBox.Show(response.ErrorMessage, "Batch Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception)
        {
        }
    }

    private async void BtnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _httpClient.PostAsync("http://localhost:5000/api/tracer/clear", null);
            TxtWcfLogs.Text = "";
            TxtSqlLogs.Text = "";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Loglar temizlenemedi: " + ex.Message);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _logTimer.Stop();
        try
        {
            if (_wcfFactory.State == CommunicationState.Opened)
            {
                _wcfFactory.Close();
            }
        }
        catch { /* ignored */ }
        base.OnClosed(e);
    }
}

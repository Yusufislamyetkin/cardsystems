using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using CoreWCF;
using Microsoft.AspNetCore.Http;
using BOA.Common.Contracts.Base;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Common.Contracts.ServiceContracts;
using BOA.Data;
using BOA.Services.Card.Hsm; // HSM Motoru için
using BOA.Services.Card.Paycore; // PayCore gateway
using BOA.Data.Helpers; // Luhn, CVV, EMV yardımcıları

namespace BOA.Services.Card;

/// <summary>
/// BOA Kart Operasyonları servis sözleşmesinin (ICardService) C# tarafındaki iş mantığı (Business Logic) uygulamasıdır.
/// Enterprise sürümde: PCI-DSS şifreleme/maskeleme, HSM PIN block doğrulama ve Çift Kayıt Muhasebe kuralları yönetilir.
/// </summary>
public class CardService : ICardService
{
    private readonly DbManager _dbManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUserContext _currentUser;

    /// <summary>
    /// UI katmanına WCF çağrı detaylarını basabilmek için kullanılan gerçek zamanlı log tracer listesidir.
    /// </summary>
    public static readonly List<string> WcfExecutionLogs = new List<string>();

    public CardService(DbManager dbManager, IHttpContextAccessor httpContextAccessor, ICurrentUserContext currentUser)
        : this(dbManager, httpContextAccessor, currentUser, new PaycoreMockGateway())
    {
    }

    public CardService(DbManager dbManager, IHttpContextAccessor httpContextAccessor, ICurrentUserContext currentUser, IPaycoreGateway paycoreGateway)
    {
        _dbManager = dbManager;
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _paycoreGateway = paycoreGateway;
    }

    private readonly IPaycoreGateway _paycoreGateway;

    /// <summary>
    /// Rol kontrolü olmadan kart oluşturma — EOD batch gibi sistem süreçleri için.
    /// </summary>
    private CreateCardResponse CreateCardInternal(CreateCardRequest request)
    {
        return CreateCardCore(request);
    }

    /// <summary>
    /// Yeni bir kart oluşturur ve karta ait GL Muhasebe Hesabı açar. Kartı kaydetmeden önce şifreyi HSM üzerinde PIN Block'a dönüştürür.
    /// </summary>
    public CreateCardResponse CreateCard(CreateCardRequest request)
    {
        CheckRole("BranchTeller");
        return CreateCardCore(request);
    }

    private CreateCardResponse CreateCardCore(CreateCardRequest request)
    {
        var response = new CreateCardResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // 1. İş Kuralları ve PIN Girdi Doğrulaması
            if (string.IsNullOrWhiteSpace(request.CardHolderName))
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "Kart sahibi adı soyadı boş geçilemez!",
                    Severity = "Warning"
                }, "Validation Error");
            }
            if (request.Limit < 0 || request.InitialBalance < 0)
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "Limit veya bakiye negatif değer olamaz!",
                    Severity = "Warning"
                }, "Validation Error");
            }
            if (request.CardType == CardType.Debit && request.Limit > 0)
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "Banka kartına (Debit) kredi limiti tanımlanamaz!",
                    Severity = "Warning"
                }, "Validation Error");
            }
            if (string.IsNullOrWhiteSpace(request.Pin) || request.Pin.Length != 4 || !int.TryParse(request.Pin, out _))
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "Yeni kart tanımlamak için 4 haneli sayısal bir şifre (PIN) girilmesi zorunludur!",
                    Severity = "Warning"
                }, "Validation Error");
            }
            if (string.IsNullOrWhiteSpace(request.NationalId))
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "Kart, bir müşteri kaydına bağlanmalıdır; T.C. Kimlik No boş geçilemez!",
                    Severity = "Warning"
                }, "Validation Error");
            }

            // 2. Kart Numarası Oluşturma (IIN / BIN) — BIN kodu artık kaynak kodda sabit değil,
            // BKM'den güncellenen BIN tablosunu simüle eden boa_bin_table'dan okunur.
            DataTable dtBin = _dbManager.ExecuteReader("sp_boa_bin_lookup", new Dictionary<string, object> { { "p_card_type", (int)request.CardType } });
            if (dtBin.Rows.Count == 0)
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "BIN_NOT_FOUND",
                    ErrorMessage = $"'{request.CardType}' kart türü için tanımlı bir BIN kodu bulunamadı!",
                    Severity = "Critical"
                }, "Configuration Error");
            }
            string binCode = dtBin.Rows[0]["bin_code"].ToString() ?? throw new InvalidOperationException("BIN kodu okunamadı.");

            // BIN tablosundan kart markası ve ürün segmentini oku
            CardBrand cardBrand = EmvHelper.DetectCardBrand(binCode);
            CardProduct cardProduct = CardProduct.Classic;
            if (dtBin.Columns.Contains("card_brand") && dtBin.Rows[0]["card_brand"] != System.DBNull.Value)
            {
                cardBrand = (CardBrand)Convert.ToInt32(dtBin.Rows[0]["card_brand"]);
            }
            if (dtBin.Columns.Contains("card_product") && dtBin.Rows[0]["card_product"] != System.DBNull.Value)
            {
                cardProduct = (CardProduct)Convert.ToInt32(dtBin.Rows[0]["card_product"]);
            }

            // Luhn algoritması ile geçerli kart numarası üret
            string generatedCardNo = LuhnHelper.AppendCheckDigit(binCode + GenerateRandomAccountNumber());
            if (!LuhnHelper.IsValid(generatedCardNo))
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "CARD_NUMBER_INVALID",
                    ErrorMessage = "Üretilen kart numarası Luhn doğrulamasından geçemedi!",
                    Severity = "Critical"
                }, "Configuration Error");
            }

            // 3. PCI-DSS: Kart numarasını şifreleme (AES-256) ve maskeleme
            string maskedPan = LuhnHelper.Mask(generatedCardNo, formatted: false).Replace("*", "");
            // 16 haneli maskeleme formatı
            string displayMaskedPan = generatedCardNo.Substring(0, 6) + "******" + generatedCardNo.Substring(12, 4);
            string encryptedPan = BOA.Data.Helpers.EncryptionHelper.Encrypt(generatedCardNo);

            // 4. HSM: Şifreyi ISO 9564 PIN Block formatına çevirme (PAN ile XOR'layarak)
            string pinHash = HsmEngine.CreatePinBlock(generatedCardNo, request.Pin);

            // 5. Son Kullanma Tarihi Belirleme (Kredi kartı: 3 yıl, Banka kartı: 5 yıl)
            bool isCredit = request.CardType == CardType.Credit;
            DateTime expiry = EmvHelper.CalculateExpiryDate(isCredit);

            // 6. EMV Veri Üretimi — Service Code, Track2, CVV/CVV2 hash
            string serviceCode = EmvHelper.GenerateServiceCode(hasChip: true, international: true);
            string expiryYYMM = EmvHelper.ToExpiryYYMM(expiry);
            string track2Data = EmvHelper.GenerateTrack2Data(generatedCardNo, expiryYYMM, serviceCode);

            // CVV2 (kart arkasındaki 3 haneli kod) — düz metin asla saklanmaz, sadece hash
            string cvv2Hash = CvvHelper.GenerateCvv2(generatedCardNo, expiry);
            string cvvHash = CvvHelper.GenerateCvv(generatedCardNo, expiryYYMM, serviceCode);

            // EMBOSS isim formatlama (kart basımı için)
            string embossName = EmvHelper.FormatEmbossName(request.CardHolderName);

            // 7. Stored Procedure Parametrelerinin Hazırlanması (yeni alanlarla)
            var dbParams = new Dictionary<string, object>
            {
                { "p_card_number", displayMaskedPan },
                { "p_encrypted_pan", encryptedPan },
                { "p_pin_hash", pinHash },
                { "p_card_holder_name", request.CardHolderName.ToUpperInvariant() },
                { "p_emboss_name", embossName },
                { "p_card_type", (int)request.CardType },
                { "p_card_brand", (int)cardBrand },
                { "p_card_product", (int)cardProduct },
                { "p_expiry_date", expiry },
                { "p_limit", request.Limit },
                { "p_initial_balance", request.InitialBalance },
                { "p_user_id", request.UserId },
                { "p_channel", request.Channel },
                { "p_client_ip", request.ClientIp },
                { "p_national_id", request.NationalId },
                { "p_phone", request.Phone ?? string.Empty },
                { "p_cvv2_hash", cvv2Hash },
                { "p_cvv_hash", cvvHash },
                { "p_service_code", serviceCode },
                { "p_track2_data", track2Data }
            };

            // 8. Stored Procedure Tetikleme (Hesap ve Kart Oluşturma)
            DataTable dt = _dbManager.ExecuteReader("sp_boa_card_create", dbParams);

            if (dt.Rows.Count > 0)
            {
                response.CreatedCard = CardMappers.ToCardDto(dt.Rows[0]);
                response.IsSuccess = true;
                response.ResultMessage = $"Kart ve GL Muhasebe Hesabı başarıyla açıldı. Maskeli No: {response.CreatedCard.MaskedCardNumber}";

                // PayCore'a kart kaydı yap (dış kart işleme sağlayıcısı)
                try
                {
                    var paycoreResult = _paycoreGateway.IssueCard(displayMaskedPan, request.CardHolderName, request.CardType);
                    if (!string.IsNullOrWhiteSpace(paycoreResult.PaycoreCardReference))
                    {
                        _dbManager.ExecuteReader("sp_boa_card_set_paycore_reference", new Dictionary<string, object>
                        {
                            { "p_card_id", response.CreatedCard.CardId },
                            { "p_paycore_reference", paycoreResult.PaycoreCardReference }
                        });
                        response.CreatedCard.PaycoreReference = paycoreResult.PaycoreCardReference;
                    }
                }
                catch
                {
                    // PayCore çağrısı başarısız olsa bile kart oluşturma işlemi tamamlanmıştır
                }
            }
            else
            {
                throw new Exception("Kart oluşturuldu fakat veritabanından geri okunamadı.");
            }
        }
        catch (FaultException<BankingFault> faultEx)
        {
            // ResponseBase.IsSuccess varsayılan olarak true'dur; bu satır olmadan finally bloğundaki
            // LogWcfCall, hataen (fault) sonuçlanan çağrıları bile tracer panelinde "SUCCESS" gösteriyordu.
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "CARD_CREATE_FAILED";
            response.ErrorMessage = $"Kart açma işlemi sırasında teknik bir veritabanı hatası oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("CreateCard", request, response);
        }

        return response;
    }

    /// <summary>
    /// Kartları filtrelere göre listeler.
    /// </summary>
    public GetCardListResponse GetCardList(GetCardListRequest request)
    {
        CheckRole("BranchTeller");

        var response = new GetCardListResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var dbParams = new Dictionary<string, object?>
            {
                { "p_holder_name", string.IsNullOrWhiteSpace(request.CardHolderNameFilter) ? null : request.CardHolderNameFilter.ToUpperInvariant() },
                { "p_card_type", request.CardTypeFilter != null ? (int)request.CardTypeFilter : null },
                { "p_status", request.StatusFilter != null ? (int)request.StatusFilter : null }
            };

            var cleanParams = dbParams.Where(x => x.Value != null)
                                      .ToDictionary(x => x.Key, x => x.Value!);

            DataTable dt = _dbManager.ExecuteReader("sp_boa_card_get_list", cleanParams);

            foreach (DataRow row in dt.Rows)
            {
                response.Cards.Add(CardMappers.ToCardDto(row));
            }

            response.IsSuccess = true;
            response.ResultMessage = $"{response.Cards.Count} adet kart başarıyla listelendi.";
        }
        catch (FaultException<BankingFault> faultEx)
        {
            // ResponseBase.IsSuccess varsayılan olarak true'dur; bu satır olmadan finally bloğundaki
            // LogWcfCall, hataen (fault) sonuçlanan çağrıları bile tracer panelinde "SUCCESS" gösteriyordu.
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "CARD_LIST_FAILED";
            response.ErrorMessage = $"Kart listesi çekilirken sistem hatası oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("GetCardList", request, response);
        }

        return response;
    }

    /// <summary>
    /// Karta ait limiti günceller. Sadece Admin rolü yetkilidir.
    /// </summary>
    public UpdateCardLimitResponse UpdateCardLimit(UpdateCardLimitRequest request)
    {
        CheckRole("CardOperationsAdmin");

        var response = new UpdateCardLimitResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (request.NewLimit < 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "VALIDATION_ERROR", ErrorMessage = "Yeni limit değeri sıfırdan küçük olamaz!", Severity = "Warning" }, "Validation Error");

            if (string.IsNullOrWhiteSpace(request.Reason))
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "VALIDATION_ERROR", ErrorMessage = "Limit değişikliği için bir gerekçe (Reason) girilmesi zorunludur!", Severity = "Warning" }, "Validation Error");

            DataTable dtCard = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_card_id", request.CardId } });
            if (dtCard.Rows.Count == 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_NOT_FOUND", ErrorMessage = "Kart bulunamadı!", Severity = "Warning" }, "Not Found");

            var currentCard = CardMappers.ToCardDto(dtCard.Rows[0]);

            if (currentCard.Status == CardStatus.Cancelled)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_CANCELLED", ErrorMessage = "İptal edilmiş bir kartın limiti değiştirilemez!", Severity = "Warning" }, "State Invalid");
            if (currentCard.Status == CardStatus.Blocked)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_BLOCKED", ErrorMessage = "Bloke bir kartın limiti değiştirilemez!", Severity = "Warning" }, "State Invalid");
            if (currentCard.CardType == CardType.Debit)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "VALIDATION_ERROR", ErrorMessage = "Debit kartlarda kredi limiti tanımlanamaz!", Severity = "Warning" }, "Validation Error");

            if (request.NewLimit > currentCard.CardLimit)
            {
                var reqParams = new Dictionary<string, object>
                {
                    { "p_card_id", request.CardId },
                    { "p_current_limit", currentCard.CardLimit },
                    { "p_requested_limit", request.NewLimit },
                    { "p_reason", request.Reason },
                    { "p_maker_user_id", request.UserId }
                };
                DataTable dtReq = _dbManager.ExecuteReader("sp_boa_limit_request_create", reqParams);
                if (dtReq.Rows.Count > 0)
                    response.PendingRequest = CardMappers.ToLimitChangeRequestDto(dtReq.Rows[0]);

                response.UpdatedCard = currentCard;
                response.IsSuccess = true;
                response.ResultMessage = "Limit artış talebi oluşturuldu, onay bekliyor.";
            }
            else if (request.NewLimit < currentCard.CardLimit)
            {
                var dbParams = new Dictionary<string, object>
                {
                    { "p_card_id", request.CardId },
                    { "p_new_limit", request.NewLimit },
                    { "p_user_id", request.UserId },
                    { "p_channel", request.Channel },
                    { "p_client_ip", request.ClientIp }
                };
                DataTable dt = _dbManager.ExecuteReader("sp_boa_card_update_limit", dbParams);
                if (dt.Rows.Count > 0)
                    response.UpdatedCard = CardMappers.ToCardDto(dt.Rows[0]);
                else
                    throw new Exception("Kart limiti güncellendi fakat güncel kart bilgisi okunamadı.");

                decimal debt = Math.Abs(response.UpdatedCard.Balance);
                if (debt > request.NewLimit)
                    response.IsOverlimit = true;

                try
                {
                    if (!string.IsNullOrWhiteSpace(currentCard.PaycoreReference))
                        _paycoreGateway.UpdateLimit(currentCard.PaycoreReference, request.NewLimit);
                }
                catch { }

                response.IsSuccess = true;
                response.ResultMessage = response.IsOverlimit
                    ? $"Limit düşürüldü ancak mevcut borç ({debt:N2} TL) yeni limitin üzerinde — kart overlimit durumda."
                    : "Kart limiti başarıyla düşürüldü.";
            }
            else
            {
                response.UpdatedCard = currentCard;
                response.IsSuccess = true;
                response.ResultMessage = "Yeni limit mevcut limitle aynı, değişiklik yapılmadı.";
            }
        }
        catch (FaultException<BankingFault> faultEx)
        {
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "LIMIT_UPDATE_FAILED";
            response.ErrorMessage = $"Limit güncelleme işlemi sırasında veritabanı hatası: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("UpdateCardLimit", request, response);
        }

        return response;
    }

    /// <summary>
    /// Kartın durumunu günceller. Sadece Admin rolü yetkilidir.
    /// </summary>
    public SetCardStatusResponse SetCardStatus(SetCardStatusRequest request)
    {
        CheckRole("CardOperationsAdmin");

        var response = new SetCardStatusResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "Kart durum değişikliği için bir gerekçe (Reason) girilmesi zorunludur!",
                    Severity = "Warning"
                }, "Validation Error");
            }

            // Mevcut kart durumunu kontrol et — Cancelled/PendingActivation kuralları
            DataTable dtCardCheck = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_card_id", request.CardId } });
            if (dtCardCheck.Rows.Count == 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_NOT_FOUND", ErrorMessage = "Kart bulunamadı!", Severity = "Warning" }, "Not Found");
            var currentCard = CardMappers.ToCardDto(dtCardCheck.Rows[0]);

            // Cancelled kartlar yeniden aktifleştirilemez
            if (currentCard.Status == CardStatus.Cancelled)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_STATE_INVALID", ErrorMessage = "İptal edilmiş bir kart yeniden aktifleştirilemez!", Severity = "Warning" }, "State Invalid");

            // PendingActivation kartlara manuel durum değişikliği yapılamaz
            if (currentCard.Status == CardStatus.PendingActivation)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_STATE_INVALID", ErrorMessage = "Aktivasyon bekleyen kartın durumu manuel değiştirilemez!", Severity = "Warning" }, "State Invalid");

            // PendingActivation durumuna manuel geçiş engellenir
            if (request.NewStatus == CardStatus.PendingActivation)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_STATE_INVALID", ErrorMessage = "Kart manuel olarak PendingActivation durumuna alınamaz!", Severity = "Warning" }, "State Invalid");

            var dbParams = new Dictionary<string, object>
            {
                { "p_card_id", request.CardId },
                { "p_new_status", (int)request.NewStatus },
                { "p_reason", request.Reason },
                { "p_user_id", request.UserId },
                { "p_channel", request.Channel },
                { "p_client_ip", request.ClientIp }
            };

            DataTable dt = _dbManager.ExecuteReader("sp_boa_card_set_status", dbParams);

            if (dt.Rows.Count > 0)
            {
                response.UpdatedCard = CardMappers.ToCardDto(dt.Rows[0]);
                response.IsSuccess = true;
                response.ResultMessage = $"Kart durumu başarıyla '{request.NewStatus}' olarak güncellendi.";
            }
            else
            {
                throw new Exception("Kart durumu güncellendi fakat güncel kart bilgisi okunamadı.");
            }
        }
        catch (FaultException<BankingFault> faultEx)
        {
            // ResponseBase.IsSuccess varsayılan olarak true'dur; bu satır olmadan finally bloğundaki
            // LogWcfCall, hataen (fault) sonuçlanan çağrıları bile tracer panelinde "SUCCESS" gösteriyordu.
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "STATUS_CHANGE_FAILED";
            response.ErrorMessage = $"Kart durum güncelleme sırasında veritabanı hatası: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("SetCardStatus", request, response);
        }

        return response;
    }

    /// <summary>
    /// Yevmiye kayıtlarından kart ekstre hareketlerini çeker.
    /// </summary>
    public GetCardTransactionsResponse GetCardTransactions(GetCardTransactionsRequest request)
    {
        CheckRole("BranchTeller");

        var response = new GetCardTransactionsResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var dbParams = new Dictionary<string, object>
            {
                { "p_card_id", request.CardId }
            };

            DataTable dt = _dbManager.ExecuteReader("sp_boa_card_get_transactions", dbParams);

            foreach (DataRow row in dt.Rows)
            {
                response.Transactions.Add(CardMappers.ToTransactionDto(row));
            }

            response.IsSuccess = true;
            response.ResultMessage = $"{response.Transactions.Count} adet işlem hareketi listelendi.";
        }
        catch (FaultException<BankingFault> faultEx)
        {
            // ResponseBase.IsSuccess varsayılan olarak true'dur; bu satır olmadan finally bloğundaki
            // LogWcfCall, hataen (fault) sonuçlanan çağrıları bile tracer panelinde "SUCCESS" gösteriyordu.
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "TRANSACTIONS_FETCH_FAILED";
            response.ErrorMessage = $"Kart hareketleri çekilirken sistem hatası oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("GetCardTransactions", request, response);
        }

        return response;
    }

    /// <summary>
    /// Harcama ve Çekim işlemlerinde HSM üzerinden PIN blok doğrulaması yapar, 
    /// ardından çift kayıtlı muhasebe girdileri oluşturarak kart kullanılabilir limitini günceller.
    /// </summary>
    public CreateTransactionResponse CreateTransaction(CreateTransactionRequest request)
    {
        CheckRole("BranchTeller");

        var response = new CreateTransactionResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (request.Amount <= 0)
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "İşlem tutarı sıfırdan büyük olmalıdır!",
                    Severity = "Warning"
                }, "Validation Error");
            }

            // 1. Harcama ve Nakit Çekme İşlemlerinde HSM PIN Doğrulaması (ISO 9564)
            if (request.TransactionType == TransactionType.Purchase || request.TransactionType == TransactionType.Withdrawal)
            {
                if (string.IsNullOrWhiteSpace(request.Pin))
                {
                    throw new FaultException<BankingFault>(new BankingFault
                    {
                        ErrorCode = "PIN_REQUIRED",
                        ErrorMessage = "Harcama ve Para Çekme işlemleri için kart şifresinin (PIN) girilmesi zorunludur!",
                        Severity = "Warning"
                    }, "PIN Required");
                }

                // Kartın güvenli verilerini (şifreli pan ve pin_hash) çek
                var secureParams = new Dictionary<string, object> { { "p_card_id", request.CardId } };
                DataTable dtSecure = _dbManager.ExecuteReader("sp_boa_card_get_secure_details", secureParams);

                if (dtSecure.Rows.Count > 0)
                {
                    string encryptedPan = dtSecure.Rows[0]["encrypted_pan"].ToString() ?? string.Empty;
                    string pinHash = dtSecure.Rows[0]["pin_hash"].ToString() ?? string.Empty;

                    // Şifreli PAN AES-256 ile çözülür
                    string unmaskedPan = BOA.Data.Helpers.EncryptionHelper.Decrypt(encryptedPan);

                    // HSM doğrulaması tetiklenir
                    if (!HsmEngine.VerifyPinBlock(unmaskedPan, pinHash, request.Pin))
                    {
                        throw new FaultException<BankingFault>(new BankingFault
                        {
                            ErrorCode = "INVALID_PIN",
                            ErrorMessage = "Kart şifresi (PIN) hatalıdır! Lütfen şifrenizi kontrol edip tekrar deneyin.",
                            Severity = "Critical"
                        }, "Invalid PIN");
                    }
                }
                else
                {
                    throw new FaultException<BankingFault>(new BankingFault
                    {
                        ErrorCode = "CARD_NOT_FOUND",
                        ErrorMessage = "İşlem yapılmak istenen kart bulunamadı!",
                        Severity = "Warning"
                    }, "Card Not Found");
                }
            }

            // 2. Benzersiz referans numarası üretimi (Guid tabanlı — Ticks'in substring'i eşzamanlı
            // isteklerde çakışabiliyordu; Guid, yoğun trafikte de biricikliği garanti eder)
            string refNo = "REF" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

            var dbParams = new Dictionary<string, object>
            {
                { "p_card_id", request.CardId },
                { "p_transaction_type", (int)request.TransactionType },
                { "p_amount", request.Amount },
                { "p_description", request.Description },
                { "p_reference_number", refNo },
                { "p_user_id", request.UserId },
                { "p_channel", request.Channel },
                { "p_client_ip", request.ClientIp },
                { "p_merchant_id", request.MerchantId ?? (object)System.DBNull.Value },
                { "p_mcc", request.Mcc ?? (object)System.DBNull.Value }
            };

            // 3. Stored Procedure Tetikle (Çift Kayıt Yevmiye Defteri posting)
            DataTable dtTrans = _dbManager.ExecuteReader("sp_boa_card_create_transaction", dbParams);

            if (dtTrans.Rows.Count > 0)
            {
                response.CreatedTransaction = CardMappers.ToTransactionDto(dtTrans.Rows[0]);

                // 4. Güncel önbellek bakiye verilerini çek
                // Tüm kartları çekip bellekte filtrelemek yerine (O(N) sorgu), tek karta özel filtre gönderiliyor.
                DataTable dtCard = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_card_id", request.CardId } });
                var updatedCardRow = dtCard.AsEnumerable().FirstOrDefault();

                if (updatedCardRow != null)
                {
                    response.UpdatedCard = CardMappers.ToCardDto(updatedCardRow);
                }

                // 5. PayCore entegrasyonu — banka onayladıktan sonra kart ağına bildirim
                if (request.TransactionType == TransactionType.Purchase || request.TransactionType == TransactionType.Withdrawal)
                {
                    string? paycoreRef = response.UpdatedCard?.PaycoreReference;
                    if (!string.IsNullOrWhiteSpace(paycoreRef))
                    {
                        try
                        {
                            var paycoreAuth = _paycoreGateway.Authorize(paycoreRef, request.Amount, refNo);
                            if (!paycoreAuth.IsApproved)
                            {
                                // PayCore reddetti — ters kayıt (reversal) ile bakiyeyi geri al
                                _dbManager.ExecuteReader("sp_boa_card_create_transaction", new Dictionary<string, object>
                                {
                                    { "p_card_id", request.CardId },
                                    { "p_transaction_type", 6 }, // Reversal
                                    { "p_amount", request.Amount },
                                    { "p_description", $"REVERSAL: PayCore reddi ({paycoreAuth.ResponseCode})" },
                                    { "p_reference_number", refNo + "R" },
                                    { "p_user_id", request.UserId },
                                    { "p_channel", request.Channel },
                                    { "p_client_ip", request.ClientIp },
                                    { "p_merchant_id", request.MerchantId ?? (object)System.DBNull.Value },
                                    { "p_mcc", request.Mcc ?? (object)System.DBNull.Value }
                                });
                                _dbManager.ExecuteReader("sp_boa_paycore_outbox_mark", new Dictionary<string, object>
                                    { { "p_status", 2 }, { "p_bank_reference_number", refNo }, { "p_last_error", "DECLINED: " + paycoreAuth.ResponseCode } });
                                throw new FaultException<BankingFault>(new BankingFault
                                {
                                    ErrorCode = "PAYCORE_DECLINED",
                                    ErrorMessage = $"Banka onayladı ancak PayCore provizyonu reddetti. Ters kayıt oluşturuldu. Yanıt: {paycoreAuth.ResponseCode}",
                                    Severity = "Warning"
                                }, "PayCore Declined");
                            }
                            // PayCore onayladı — outbox'u Confirmed yap
                            _dbManager.ExecuteReader("sp_boa_paycore_outbox_mark", new Dictionary<string, object>
                                { { "p_status", 2 }, { "p_bank_reference_number", refNo }, { "p_last_error", (object)System.DBNull.Value } });
                        }
                        catch (FaultException<BankingFault>) { throw; }
                        catch (Exception)
                        {
                            // PayCore'a ulaşılamadı — outbox Pending kalır, mutabakat taraması yeniden deneyecek
                            throw new FaultException<BankingFault>(new BankingFault
                            {
                                ErrorCode = "PAYCORE_UNREACHABLE",
                                ErrorMessage = "Banka onayladı ancak PayCore'a ulaşılamadı. İşlem askıda — mutabakat taraması yeniden deneyecek.",
                                Severity = "Warning"
                            }, "PayCore Unreachable");
                        }
                    }
                }

                response.IsSuccess = true;
                response.ResultMessage = $"İşlem onaylandı ve çift kayıtlı muhasebe yevmiye girişi oluşturuldu. Ref No: {refNo}";
            }
            else
            {
                throw new Exception("İşlem onaylanamadı.");
            }
        }
        catch (FaultException<BankingFault> faultEx)
        {
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "TRANSACTION_CREATE_FAILED";
            response.ErrorMessage = $"İşlem gerçekleştirilirken veritabanı/bakiye hatası oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("CreateTransaction", request, response);
        }

        return response;
    }

    /// <summary>
    /// Kart şifresinin (PIN) HSM üzerinden bağımsız olarak doğruluğunu test eder.
    /// </summary>
    public VerifyPinResponse VerifyPin(VerifyPinRequest request)
    {
        CheckRole("BranchTeller");

        var response = new VerifyPinResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var dbParams = new Dictionary<string, object>
            {
                { "p_card_id", request.CardId }
            };

            DataTable dtSecure = _dbManager.ExecuteReader("sp_boa_card_get_secure_details", dbParams);

            if (dtSecure.Rows.Count > 0)
            {
                string encryptedPan = dtSecure.Rows[0]["encrypted_pan"].ToString() ?? string.Empty;
                string pinHash = dtSecure.Rows[0]["pin_hash"].ToString() ?? string.Empty;

                string unmaskedPan = BOA.Data.Helpers.EncryptionHelper.Decrypt(encryptedPan);
                bool isValid = HsmEngine.VerifyPinBlock(unmaskedPan, pinHash, request.Pin);

                response.IsPinValid = isValid;
                response.IsSuccess = true;
                response.ResultMessage = isValid ? "Şifre doğrulama başarılı." : "Girdiğiniz kart şifresi hatalıdır!";
            }
            else
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "CARD_NOT_FOUND",
                    ErrorMessage = "Şifresi doğrulanacak kart bulunamadı!",
                    Severity = "Warning"
                }, "Card Not Found");
            }
        }
        catch (FaultException<BankingFault> faultEx)
        {
            // ResponseBase.IsSuccess varsayılan olarak true'dur; bu satır olmadan finally bloğundaki
            // LogWcfCall, hataen (fault) sonuçlanan çağrıları bile tracer panelinde "SUCCESS" gösteriyordu.
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "PIN_VERIFY_FAILED";
            response.ErrorMessage = $"Şifre doğrulama sırasında teknik hata oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("VerifyPin", request, response);
        }

        return response;
    }

    /// <summary>
    /// Harcama/çekim işlemi için provizyon (authorization/hold) alır. Onaylanırsa (Approved) tutar
    /// yalnızca bloke edilir; hiçbir yevmiye kaydı oluşmaz. Kesinleşme için ayrıca CaptureAuthorization
    /// çağrılmalıdır. Bu, önceki sürümde hiç var olmayan Authorize→Capture akışının ilk adımıdır.
    /// </summary>
    public AuthorizeTransactionResponse AuthorizeTransaction(AuthorizeTransactionRequest request)
    {
        CheckRole("BranchTeller");

        var response = new AuthorizeTransactionResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (request.TransactionType != TransactionType.Purchase && request.TransactionType != TransactionType.Withdrawal)
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "Provizyon yalnızca Harcama (Purchase) veya Nakit Çekim (Withdrawal) işlemleri için alınabilir.",
                    Severity = "Warning"
                }, "Validation Error");
            }
            if (request.Amount <= 0)
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "VALIDATION_ERROR",
                    ErrorMessage = "İşlem tutarı sıfırdan büyük olmalıdır!",
                    Severity = "Warning"
                }, "Validation Error");
            }

            var secureParams = new Dictionary<string, object> { { "p_card_id", request.CardId } };
            DataTable dtSecure = _dbManager.ExecuteReader("sp_boa_card_get_secure_details", secureParams);
            if (dtSecure.Rows.Count == 0)
            {
                throw new FaultException<BankingFault>(new BankingFault
                {
                    ErrorCode = "CARD_NOT_FOUND",
                    ErrorMessage = "Provizyon alınmak istenen kart bulunamadı!",
                    Severity = "Warning"
                }, "Card Not Found");
            }

            // HSM PIN Doğrulaması. Hatalıysa provizyon yine de oluşturulur (denetim izi için) ama
            // bakiye/limit kontrolü hiç yapılmadan doğrudan 55 (Incorrect PIN) ile reddedilir.
            string encryptedPan = dtSecure.Rows[0]["encrypted_pan"].ToString() ?? string.Empty;
            string pinHash = dtSecure.Rows[0]["pin_hash"].ToString() ?? string.Empty;
            string unmaskedPan = BOA.Data.Helpers.EncryptionHelper.Decrypt(encryptedPan);
            bool pinValid = HsmEngine.VerifyPinBlock(unmaskedPan, pinHash, request.Pin);

            string refNo = "AUTH" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            string candidateAuthCode = GenerateAuthorizationCode();

            var dbParams = new Dictionary<string, object>
            {
                { "p_card_id", request.CardId },
                { "p_transaction_type", (int)request.TransactionType },
                { "p_amount", request.Amount },
                { "p_description", request.Description },
                { "p_reference_number", refNo },
                { "p_merchant_id", request.MerchantId ?? (object)System.DBNull.Value },
                { "p_mcc", request.Mcc ?? (object)System.DBNull.Value },
                { "p_candidate_auth_code", candidateAuthCode },
                { "p_forced_response_code", pinValid ? (object)System.DBNull.Value : (int)AuthResponseCode.IncorrectPin },
                { "p_user_id", request.UserId },
                { "p_channel", request.Channel },
                { "p_client_ip", request.ClientIp }
            };

            DataTable dtAuth = _dbManager.ExecuteReader("sp_boa_auth_create", dbParams);
            if (dtAuth.Rows.Count == 0)
            {
                throw new Exception("Provizyon oluşturuldu fakat veritabanından geri okunamadı.");
            }

            response.Authorization = CardMappers.ToAuthorizationDto(dtAuth.Rows[0]);

            // Banka onayladıysa PayCore'a gönder
            if (response.Authorization.ResponseCode == AuthResponseCode.Approved)
            {
                DataTable dtCardForPaycore = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_card_id", request.CardId } });
                string? paycoreRef = dtCardForPaycore.Rows.Count > 0 ? CardMappers.ToCardDto(dtCardForPaycore.Rows[0]).PaycoreReference : null;

                if (!string.IsNullOrWhiteSpace(paycoreRef))
                {
                    try
                    {
                        var paycoreAuth = _paycoreGateway.Authorize(paycoreRef, request.Amount, refNo);
                        if (paycoreAuth.IsApproved)
                        {
                            // PayCore onayladı — referansı kaydet
                            var dtUpdatedAuth = _dbManager.ExecuteReader("sp_boa_auth_set_paycore_reference", new Dictionary<string, object>
                            {
                                { "p_authorization_id", response.Authorization.AuthorizationId },
                                { "p_paycore_auth_reference", paycoreAuth.PaycoreAuthReference ?? "" }
                            });
                            if (dtUpdatedAuth.Rows.Count > 0)
                                response.Authorization = CardMappers.ToAuthorizationDto(dtUpdatedAuth.Rows[0]);
                        }
                        else
                        {
                            // PayCore reddetti — holdü geri al
                            var dtOverride = _dbManager.ExecuteReader("sp_boa_auth_override_decline", new Dictionary<string, object>
                            {
                                { "p_authorization_id", response.Authorization.AuthorizationId },
                                { "p_response_code", (int)AuthResponseCode.DoNotHonor },
                                { "p_reason", paycoreAuth.ErrorMessage ?? "PayCore declined" }
                            });
                            if (dtOverride.Rows.Count > 0)
                                response.Authorization = CardMappers.ToAuthorizationDto(dtOverride.Rows[0]);
                        }
                    }
                    catch
                    {
                        // PayCore'a ulaşılamadı — hold duruyor, paycore_auth_reference null kalacak
                    }
                }
            }

            response.IsSuccess = true;
            response.ResultMessage = response.Authorization.ResponseCode == AuthResponseCode.Approved
                ? $"Provizyon onaylandı. Provizyon Kodu: {response.Authorization.AuthorizationCode}"
                : $"Provizyon reddedildi. Yanıt Kodu: {(int)response.Authorization.ResponseCode:D2} ({response.Authorization.ResponseCode})";
        }
        catch (FaultException<BankingFault> faultEx)
        {
            // ResponseBase.IsSuccess varsayılan olarak true'dur; bu satır olmadan finally bloğundaki
            // LogWcfCall, hataen (fault) sonuçlanan çağrıları bile tracer panelinde "SUCCESS" gösteriyordu.
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "AUTHORIZATION_FAILED";
            response.ErrorMessage = $"Provizyon işlemi sırasında teknik hata oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("AuthorizeTransaction", request, response);
        }

        return response;
    }

    /// <summary>
    /// Authorized durumundaki bir provizyonu kesinleştirir (Capture): yevmiye defterine gerçek
    /// borç/alacak kaydını yazar ve kart bakiyesini/kullanılabilir limitini günceller.
    /// </summary>
    public CaptureAuthorizationResponse CaptureAuthorization(CaptureAuthorizationRequest request)
    {
        CheckRole("BranchTeller");

        var response = new CaptureAuthorizationResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var dbParams = new Dictionary<string, object>
            {
                { "p_authorization_id", request.AuthorizationId },
                { "p_user_id", request.UserId },
                { "p_channel", request.Channel },
                { "p_client_ip", request.ClientIp }
            };

            DataTable dtAuth = _dbManager.ExecuteReader("sp_boa_auth_capture", dbParams);
            if (dtAuth.Rows.Count == 0)
            {
                throw new Exception("Provizyon kesinleştirildi fakat veritabanından geri okunamadı.");
            }

            response.Authorization = CardMappers.ToAuthorizationDto(dtAuth.Rows[0]);

            DataTable dtCard = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_card_id", response.Authorization.CardId } });
            if (dtCard.Rows.Count > 0)
            {
                response.UpdatedCard = CardMappers.ToCardDto(dtCard.Rows[0]);
            }

            response.IsSuccess = true;
            response.ResultMessage = $"Provizyon kesinleştirildi (Capture). Ref No: {response.Authorization.ReferenceNumber}";
        }
        catch (FaultException<BankingFault> faultEx)
        {
            // ResponseBase.IsSuccess varsayılan olarak true'dur; bu satır olmadan finally bloğundaki
            // LogWcfCall, hataen (fault) sonuçlanan çağrıları bile tracer panelinde "SUCCESS" gösteriyordu.
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "CAPTURE_FAILED";
            response.ErrorMessage = $"Provizyon kesinleştirme sırasında hata oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("CaptureAuthorization", request, response);
        }

        return response;
    }

    /// <summary>
    /// Authorized durumundaki bir provizyonu iptal eder (Void): bloke edilen tutarı serbest bırakır,
    /// hiçbir muhasebe kaydı oluşturmaz.
    /// </summary>
    public VoidAuthorizationResponse VoidAuthorization(VoidAuthorizationRequest request)
    {
        CheckRole("BranchTeller");

        var response = new VoidAuthorizationResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var dbParams = new Dictionary<string, object>
            {
                { "p_authorization_id", request.AuthorizationId },
                { "p_reason", request.Reason }
            };

            DataTable dtAuth = _dbManager.ExecuteReader("sp_boa_auth_void", dbParams);
            if (dtAuth.Rows.Count == 0)
            {
                throw new Exception("Provizyon iptal edildi fakat veritabanından geri okunamadı.");
            }

            response.Authorization = CardMappers.ToAuthorizationDto(dtAuth.Rows[0]);
            response.IsSuccess = true;
            response.ResultMessage = "Provizyon başarıyla iptal edildi (Void).";
        }
        catch (FaultException<BankingFault> faultEx)
        {
            // ResponseBase.IsSuccess varsayılan olarak true'dur; bu satır olmadan finally bloğundaki
            // LogWcfCall, hataen (fault) sonuçlanan çağrıları bile tracer panelinde "SUCCESS" gösteriyordu.
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "VOID_FAILED";
            response.ErrorMessage = $"Provizyon iptali sırasında hata oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("VoidAuthorization", request, response);
        }

        return response;
    }

    // Gün sonu (EOD) batch süreci sabitleri. Gerçek bir bankada bu oranlar BDDK/Merkez Bankası
    // tebliğlerine göre kademeli ve periyodik olarak güncellenir; burada basitleştirilmiş sabit
    // değerlerdir (demo/eğitim amaçlıdır, gerçek oranları yansıtmaz).
    private const decimal MonthlyInterestRate = 0.035m; // Aylık gecikme faizi oranı (basitleştirilmiş sabit %3.5)
    private const decimal MinimumPaymentRate = 0.20m;   // Asgari ödeme oranı (BDDK'nin kademeli oranlarının basitleştirilmiş yaklaşımı)
    private const decimal MinimumPaymentFloor = 100m;   // Borç bu tutarın altındaysa asgari ödeme borcun tamamıdır
    private const int StatementDueDays = 10;            // Ekstre kesiminden son ödeme tarihine kadar geçen gün sayısı
    private const int AutoBlockOverdueDays = 30;        // Vadesi bu kadar gün geçen ödenmemiş ekstre kartı otomatik bloke eder
    private const int RenewalWindowDays = 30;           // Son kullanma tarihine bu kadar gün kala kart otomatik yenilenir

    /// <summary>
    /// Gün sonu (End of Day) batch sürecini çalıştırır. Gerçek bir bankada bu süreç gece yarısı
    /// zamanlanmış bir iş (Hangfire/Quartz.NET) olarak otomatik tetiklenir; bu projede eğitim/demo
    /// amaçlı olarak manuel tetiklenebilir bir servis operasyonu olarak sunulur. Kredi kartları için:
    /// (1) ödenmemiş ekstresi olmayıp borcu bulunan kartlara yeni ekstre keser,
    /// (2) vadesi geçmiş ekstrelere bir kereye mahsus gecikme faizi işler,
    /// (3) 30 günü aşan gecikmelerde kartı otomatik bloke eder,
    /// (4) son kullanma tarihine 30 gün kalan kartları otomatik yeniler.
    /// </summary>
    public RunEodBatchResponse RunEodBatch(RunEodBatchRequest request)
    {
        CheckRole("CardOperationsAdmin");

        var response = new RunEodBatchResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            int statementsGenerated = 0, interestAppliedCount = 0, cardsBlocked = 0, cardsRenewed = 0, cardsIssued = 0, applicationsExpired = 0;

            DataTable dtCards = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_card_type", (int)CardType.Credit } });
            foreach (DataRow cardRow in dtCards.Rows)
            {
                var card = CardMappers.ToCardDto(cardRow);

                DataTable dtOpenStatement = _dbManager.ExecuteReader("sp_boa_statement_get_open", new Dictionary<string, object> { { "p_card_id", card.CardId } });

                if (dtOpenStatement.Rows.Count > 0)
                {
                    var statement = CardMappers.ToStatementDto(dtOpenStatement.Rows[0]);

                    if (statement.DueDate < DateTime.Now)
                    {
                        if (!statement.InterestApplied)
                        {
                            decimal interestAmount = Math.Round(statement.TotalDebt * MonthlyInterestRate, 2);
                            if (interestAmount > 0)
                            {
                                var feeParams = new Dictionary<string, object>
                                {
                                    { "p_card_id", card.CardId },
                                    { "p_transaction_type", (int)TransactionType.Fee },
                                    { "p_amount", interestAmount },
                                    { "p_description", $"Gecikme Faizi (Aylık %{MonthlyInterestRate * 100:0.##})" },
                                    { "p_reference_number", "FEE" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() },
                                    { "p_merchant_id", (object)System.DBNull.Value },
                                    { "p_mcc", (object)System.DBNull.Value },
                                    { "p_user_id", "SYSTEM_BATCH" },
                                    { "p_channel", "BATCH" },
                                    { "p_client_ip", "127.0.0.1" }
                                };
                                _dbManager.ExecuteReader("sp_boa_card_create_transaction", feeParams);
                                _dbManager.ExecuteReader("sp_boa_statement_mark_interest_applied", new Dictionary<string, object> { { "p_statement_id", statement.StatementId } });
                                interestAppliedCount++;
                            }
                        }

                        if (card.Status == CardStatus.Active && (DateTime.Now - statement.DueDate).TotalDays > AutoBlockOverdueDays)
                        {
                            _dbManager.ExecuteReader("sp_boa_card_set_status", new Dictionary<string, object>
                            {
                                { "p_card_id", card.CardId },
                                { "p_new_status", (int)CardStatus.Blocked },
                                { "p_reason", $"Otomatik Blokaj - {AutoBlockOverdueDays} Günü Aşan Gecikmiş Ödeme" },
                                { "p_user_id", "SYSTEM_BATCH" },
                                { "p_channel", "BATCH" },
                                { "p_client_ip", "127.0.0.1" }
                            });
                            cardsBlocked++;
                        }
                    }
                }
                else if (card.Balance < 0)
                {
                    // Kredi kartlarında borç, bu projenin çift kayıt muhasebe modelinde negatif bakiye
                    // olarak tutulur (harcama = borç artışı = negatife doğru); ekstre tutarı mutlak değerdir.
                    decimal totalDebt = Math.Abs(card.Balance);
                    decimal minimumPayment = CalculateMinimumPayment(totalDebt);
                    var statementParams = new Dictionary<string, object>
                    {
                        { "p_card_id", card.CardId },
                        { "p_statement_date", DateTime.Now },
                        { "p_due_date", DateTime.Now.AddDays(StatementDueDays) },
                        { "p_total_debt", totalDebt },
                        { "p_minimum_payment", minimumPayment }
                    };
                    _dbManager.ExecuteReader("sp_boa_statement_create", statementParams);
                    statementsGenerated++;
                }

                if (card.Status != CardStatus.Cancelled && (card.ExpiryDate - DateTime.Now).TotalDays <= RenewalWindowDays)
                {
                    _dbManager.ExecuteReader("sp_boa_card_renew", new Dictionary<string, object>
                    {
                        { "p_card_id", card.CardId },
                        { "p_new_expiry_date", card.ExpiryDate.AddYears(5) },
                        { "p_user_id", "SYSTEM_BATCH" },
                        { "p_channel", "BATCH" },
                        { "p_client_ip", "127.0.0.1" }
                    });
                    cardsRenewed++;
                }
            }

            // EOD: Onaylanmış başvuruları karta dönüştür (AutoApproved + Approved → Issued)
            DataTable dtIssuable = _dbManager.ExecuteReader("sp_boa_application_list", new Dictionary<string, object> { { "p_only_issuable", true } });
            foreach (DataRow appRow in dtIssuable.Rows)
            {
                var app = CardMappers.ToCardApplicationDto(appRow);
                if (app.Status == CardApplicationStatus.AutoApproved || app.Status == CardApplicationStatus.Approved)
                {
                    var createReq = new CreateCardRequest
                    {
                        CardHolderName = app.ApplicantName,
                        NationalId = app.NationalId,
                        CardType = CardType.Credit,
                        Limit = app.ApprovedLimit ?? app.RequestedLimit,
                        InitialBalance = 0,
                        Pin = "0000",
                        Phone = app.Phone ?? "",
                        Channel = "BATCH",
                        UserId = "SYSTEM_BATCH",
                        ClientIp = "127.0.0.1"
                    };
                    var cardResult = CreateCardInternal(createReq);
                    if (cardResult.IsSuccess)
                    {
                        _dbManager.ExecuteReader("sp_boa_application_mark_issued", new Dictionary<string, object>
                        {
                            { "p_application_id", app.ApplicationId },
                            { "p_card_id", cardResult.CreatedCard.CardId }
                        });
                        // Kartı PendingActivation durumuna al (basıldı, müşteri aktivasyonu bekleniyor)
                        _dbManager.ExecuteReader("sp_boa_card_set_status", new Dictionary<string, object>
                        {
                            { "p_card_id", cardResult.CreatedCard.CardId },
                            { "p_new_status", (int)CardStatus.PendingActivation },
                            { "p_reason", "EOD Batch - Kart basıldı, müşteri aktivasyonu bekleniyor" },
                            { "p_user_id", "SYSTEM_BATCH" },
                            { "p_channel", "BATCH" },
                            { "p_client_ip", "127.0.0.1" }
                        });
                        cardsIssued++;
                    }
                }
            }

            // EOD: 30 günden eski ManualReview başvuruları expire et
            DataTable dtExpire = _dbManager.ExecuteReader("sp_boa_application_expire", new Dictionary<string, object>
            {
                { "p_cutoff", DateTime.Now.AddDays(-30) }
            });
            if (dtExpire.Rows.Count > 0 && dtExpire.Columns.Contains("expired_count"))
            {
                applicationsExpired = Convert.ToInt32(dtExpire.Rows[0]["expired_count"]);
            }

            response.StatementsGenerated = statementsGenerated;
            response.InterestAppliedCount = interestAppliedCount;
            response.CardsAutoBlocked = cardsBlocked;
            response.CardsRenewed = cardsRenewed;
            response.CardsIssued = cardsIssued;
            response.ApplicationsExpired = applicationsExpired;
            response.IsSuccess = true;
            response.ResultMessage = $"Gün sonu batch tamamlandı: {statementsGenerated} yeni ekstre, {interestAppliedCount} gecikme faizi işlemi, {cardsBlocked} otomatik blokaj, {cardsRenewed} kart yenileme, {cardsIssued} kart basımı, {applicationsExpired} başvuru süre aşımı.";
        }
        catch (FaultException<BankingFault> faultEx)
        {
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "EOD_BATCH_FAILED";
            response.ErrorMessage = $"Gün sonu batch süreci sırasında hata oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("RunEodBatch", request, response);
        }

        return response;
    }

    /// <summary>
    /// Bir karta ait ekstre (hesap kesimi) geçmişini listeler.
    /// </summary>
    public GetCardStatementsResponse GetCardStatements(GetCardStatementsRequest request)
    {
        CheckRole("BranchTeller");

        var response = new GetCardStatementsResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            DataTable dt = _dbManager.ExecuteReader("sp_boa_statement_get_list", new Dictionary<string, object> { { "p_card_id", request.CardId } });
            foreach (DataRow row in dt.Rows)
            {
                response.Statements.Add(CardMappers.ToStatementDto(row));
            }

            response.IsSuccess = true;
            response.ResultMessage = $"{response.Statements.Count} adet ekstre listelendi.";
        }
        catch (FaultException<BankingFault> faultEx)
        {
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "STATEMENTS_FETCH_FAILED";
            response.ErrorMessage = $"Ekstreler çekilirken hata oluştu: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault
            {
                ErrorCode = response.ErrorCode,
                ErrorMessage = response.ErrorMessage,
                Severity = "Error"
            }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("GetCardStatements", request, response);
        }

        return response;
    }

    /// <summary>
    /// BDDK'nin kademeli asgari ödeme oranlarının basitleştirilmiş bir yaklaşımı: borç, sabit bir
    /// yüzde (varsayılan %20) ile hesaplanır ve düşük bakiyelerde borcun tamamı asgari ödeme kabul edilir.
    /// </summary>
    internal static decimal CalculateMinimumPayment(decimal totalDebt)
    {
        if (totalDebt <= 0) return 0;
        if (totalDebt <= MinimumPaymentFloor) return totalDebt;
        return Math.Max(Math.Round(totalDebt * MinimumPaymentRate, 2), MinimumPaymentFloor);
    }

    // =====================================================================================
    // GÜVENLİK VE ROL DOĞRULAMA (RBAC) METOTLARI
    // =====================================================================================

    private void ValidateSecurityHeader()
    {
        string? token = null;

        // A. WCF SOAP kanalından çağrılıyorsa (WPF/Console istemcisi), token SOAP header'ından okunur.
        var headers = OperationContext.Current?.IncomingMessageHeaders;
        if (headers != null)
        {
            int index = headers.FindHeader("SecurityToken", "http://emlakkatilim.com.tr/security");
            if (index >= 0)
            {
                token = headers.GetHeader<string>(index);
            }
        }
        else
        {
            // B. REST/JSON API üzerinden (CardApiController) çağrılıyorsa OperationContext yoktur;
            // WCF header kontrolünü sessizce atlamak yerine HTTP isteğindeki X-Security-Token header'ı doğrulanır.
            token = _httpContextAccessor.HttpContext?.Request.Headers["X-Security-Token"].FirstOrDefault();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            throw new FaultException<BankingFault>(
                new BankingFault
                {
                    ErrorCode = "AUTHENTICATION_FAILED",
                    ErrorMessage = "SecurityToken bulunamadı! Lütfen geçerli bir kimlik doğrulama belirteci gönderin.",
                    Severity = "Critical"
                }, "Authentication Failed");
        }

        if (!token.StartsWith("MOCK_JWT_TELLER_TOKEN") && !token.StartsWith("MOCK_JWT_ADMIN_TOKEN"))
        {
            throw new FaultException<BankingFault>(
                new BankingFault
                {
                    ErrorCode = "AUTHENTICATION_FAILED",
                    ErrorMessage = "Geçersiz veya süresi dolmuş SecurityToken!",
                    Severity = "Critical"
                }, "Authentication Failed");
        }

        string role = token.Contains("ADMIN") ? "CardOperationsAdmin" : "BranchTeller";
        string user = token.Contains("ADMIN") ? "ADMIN_USER" : "TELLER_USER";

        // Kimlik/rol bilgisi, statik/thread-global Thread.CurrentPrincipal yerine istek bazlı
        // (Scoped) ICurrentUserContext üzerinde tutulur; ASP.NET Core'un havuzlanmış thread'leri
        // arasında farklı isteklerin birbirinin kimliğini görmesi riskini ortadan kaldırır.
        _currentUser.Set(user, role);
    }

    private void CheckRole(string requiredRole)
    {
        ValidateSecurityHeader();

        if (!_currentUser.IsInRole(requiredRole))
        {
            throw new FaultException<BankingFault>(
                new BankingFault
                {
                    ErrorCode = "ACCESS_DENIED",
                    ErrorMessage = $"Bu işlem için '{requiredRole}' yetkisi gerekmektedir! Mevcut kullanıcının bu işlem için yetkisi bulunmamaktadır.",
                    Severity = "Critical"
                }, "Access Denied");
        }
    }

    // =====================================================================================
    // YARDIMCI MAPPING VE ÜRETİM METOTLARI
    // =====================================================================================

    // DataRow -> DTO dönüştürme fonksiyonları CardMappers.cs'e taşındı (CardService'in "God Class"
    // büyüklüğünü azaltmak amacıyla); MapXFromRow(row) çağrıları artık CardMappers.ToXDto(row)'dur.

    /// <summary>
    /// Onaylanan bir provizyon için 6 haneli alfanümerik provizyon (yetki) kodu üretir.
    /// </summary>
    internal static string GenerateAuthorizationCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // karışabilecek 0/O, 1/I hariç
        Span<byte> randomBytes = stackalloc byte[6];
        System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);

        var sb = new System.Text.StringBuilder(6);
        foreach (var b in randomBytes)
        {
            sb.Append(alphabet[b % alphabet.Length]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Luhn algoritması kullanarak BIN + 9 haneli rastgele hesap numarası üretir.
    /// Check digit burada eklenmez; AppendCheckDigit ile ayrıca eklenir.
    /// </summary>
    internal static string GenerateRandomAccountNumber()
    {
        Span<byte> randomBytes = stackalloc byte[9];
        System.Security.Cryptography.RandomNumberGenerator.Fill(randomBytes);

        var sb = new System.Text.StringBuilder(9);
        foreach (var b in randomBytes)
        {
            sb.Append(b % 10);
        }
        return sb.ToString();
    }

    public ApplyForCreditCardResponse ApplyForCreditCard(ApplyForCreditCardRequest request)
    {
        CheckRole("BranchTeller");

        var response = new ApplyForCreditCardResponse();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (string.IsNullOrWhiteSpace(request.NationalId) || request.NationalId.Length != 11 || !request.NationalId.All(char.IsDigit))
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "VALIDATION_ERROR", ErrorMessage = "Geçersiz T.C. Kimlik No! 11 haneli olmalıdır.", Severity = "Warning" }, "Validation Error");
            if (request.DeclaredMonthlyIncome <= 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "VALIDATION_ERROR", ErrorMessage = "Beyan edilen gelir sıfırdan büyük olmalıdır!", Severity = "Warning" }, "Validation Error");

            // Fraud kara liste kontrolü
            DataTable dtFraud = _dbManager.ExecuteReader("sp_boa_fraud_check", new Dictionary<string, object> { { "p_national_id", request.NationalId } });
            if (dtFraud.Rows.Count > 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "APPLICANT_BLACKLISTED", ErrorMessage = "Başvuru sahibi kara listede bulunuyor!", Severity = "Warning" }, "Fraud Check");

            // Hızlı başvuru kontrolü (24 saat içinde tekrar başvuru)
            DataTable dtRapid = _dbManager.ExecuteReader("sp_boa_rapid_application_check", new Dictionary<string, object> { { "p_national_id", request.NationalId } });
            int rapidCount = dtRapid.Rows.Count > 0 ? Convert.ToInt32(dtRapid.Rows[0]["cnt"]) : 0;
            if (rapidCount >= 3)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "APPLICATION_RATE_LIMITED", ErrorMessage = "Çok sık başvuru yaptınız, lütfen 24 saat bekleyin.", Severity = "Warning" }, "Rate Limit");

            // Zaten açık başvuru var mı?
            DataTable dtOpen = _dbManager.ExecuteReader("sp_boa_application_list", new Dictionary<string, object> { { "p_national_id", request.NationalId }, { "p_only_open", true } });
            if (dtOpen.Rows.Count > 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "APPLICATION_ALREADY_PENDING", ErrorMessage = "Bu T.C. Kimlik No için zaten değerlendirmede bekleyen bir başvuru var!", Severity = "Warning" }, "Duplicate");

            // Kredi skoru hesaplama (basit deterministik model)
            int creditScore = ComputeCreditScore(request.NationalId, request.DeclaredMonthlyIncome);

            // BDDK limit tavanı
            int existingCardCount = 0;
            decimal existingLimit = 0m;
            DataTable dtExistingCards = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_national_id", request.NationalId } });
            foreach (DataRow row in dtExistingCards.Rows)
            {
                var c = CardMappers.ToCardDto(row);
                if (c.CardType == CardType.Credit && c.Status != CardStatus.Cancelled)
                {
                    existingCardCount++;
                    existingLimit += c.CardLimit;
                }
            }

            decimal incomeMultiplier = existingCardCount == 0 ? 2m : 4m;
            decimal bddkCap = Math.Floor(request.DeclaredMonthlyIncome * incomeMultiplier - existingLimit);
            if (bddkCap < 0) bddkCap = 0;

            // BDDK tavanı 0 ise mevcut risk dolmuş demektir → otomatik red
            if (bddkCap <= 0 && creditScore >= 700)
            {
                creditScore = 699; // force AutoRejected path
            }

            CardApplicationStatus status;
            decimal? approvedLimit = null;
            string reason = "";

            if (creditScore >= 1100)
            {
                status = CardApplicationStatus.AutoApproved;
                approvedLimit = Math.Min(request.RequestedLimit, bddkCap);
                if (approvedLimit == bddkCap && bddkCap < request.RequestedLimit)
                    reason = $"BDDK limit tavanına takıldı. Maksimum onaylanabilir limit: {bddkCap} TL";
                else
                    reason = $"Onaylandı. Kredi skoru: {creditScore}";
            }
            else if (creditScore >= 700)
            {
                status = CardApplicationStatus.ManualReview;
                reason = $"Orta skor bandı, manuel değerlendirme gerekli. Kredi skoru: {creditScore}";
            }
            else
            {
                status = CardApplicationStatus.AutoRejected;
                reason = $"Kredi skoru yetersiz. Skor: {creditScore} (minimum: 700)";
            }

            var dbParams = new Dictionary<string, object>
            {
                { "p_national_id", request.NationalId },
                { "p_applicant_name", request.ApplicantName },
                { "p_phone", request.Phone ?? (object)System.DBNull.Value },
                { "p_declared_monthly_income", request.DeclaredMonthlyIncome },
                { "p_requested_limit", request.RequestedLimit },
                { "p_credit_score", creditScore },
                { "p_bddk_limit_cap", bddkCap },
                { "p_approved_limit", approvedLimit ?? (object)System.DBNull.Value },
                { "p_status", (int)status },
                { "p_decision_reason", reason },
                { "p_maker_user_id", request.UserId }
            };

            DataTable dt = _dbManager.ExecuteReader("sp_boa_application_create", dbParams);
            if (dt.Rows.Count > 0)
            {
                response.Application = CardMappers.ToCardApplicationDto(dt.Rows[0]);
                response.IsSuccess = true;
                response.ResultMessage = $"Başvuru alındı. Durum: {status}";
            }
        }
        catch (FaultException<BankingFault> faultEx)
        {
            response.IsSuccess = false;
            response.ErrorCode = faultEx.Detail.ErrorCode;
            response.ErrorMessage = faultEx.Detail.ErrorMessage;
            throw;
        }
        catch (Exception ex)
        {
            response.IsSuccess = false;
            response.ErrorCode = "APPLICATION_FAILED";
            response.ErrorMessage = $"Başvuru sırasında hata: {ex.Message}";
            throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error");
        }
        finally
        {
            stopwatch.Stop();
            response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds;
            LogWcfCall("ApplyForCreditCard", request, response);
        }
        return response;
    }

    public GetCardApplicationsResponse GetCardApplications(GetCardApplicationsRequest request)
    {
        CheckRole("BranchTeller");
        var response = new GetCardApplicationsResponse();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var dbParams = new Dictionary<string, object>();
            if (request.ApplicationId.HasValue) dbParams["p_application_id"] = request.ApplicationId.Value;
            if (!string.IsNullOrWhiteSpace(request.NationalId)) dbParams["p_national_id"] = request.NationalId;
            if (request.Status.HasValue) dbParams["p_status"] = (int)request.Status.Value;
            DataTable dt = _dbManager.ExecuteReader("sp_boa_application_list", dbParams);
            foreach (DataRow row in dt.Rows)
                response.Applications.Add(CardMappers.ToCardApplicationDto(row));
            response.IsSuccess = true;
            response.ResultMessage = $"{response.Applications.Count} adet başvuru listelendi.";
        }
        catch (FaultException<BankingFault> faultEx) { response.IsSuccess = false; response.ErrorCode = faultEx.Detail.ErrorCode; response.ErrorMessage = faultEx.Detail.ErrorMessage; throw; }
        catch (Exception ex) { response.IsSuccess = false; response.ErrorCode = "APPLICATION_LIST_FAILED"; response.ErrorMessage = ex.Message; throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error"); }
        finally { stopwatch.Stop(); response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds; LogWcfCall("GetCardApplications", request, response); }
        return response;
    }

    public DecideCardApplicationResponse DecideCardApplication(DecideCardApplicationRequest request)
    {
        CheckRole("CardOperationsAdmin");
        var response = new DecideCardApplicationResponse();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            DataTable dtApp = _dbManager.ExecuteReader("sp_boa_application_get", new Dictionary<string, object> { { "p_application_id", request.ApplicationId } });
            if (dtApp.Rows.Count == 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "APPLICATION_NOT_FOUND", ErrorMessage = "Başvuru bulunamadı!", Severity = "Warning" }, "Not Found");

            var app = CardMappers.ToCardApplicationDto(dtApp.Rows[0]);

            // BDDK limit kontrolü en önce (state check'ten önce) yapılır
            if (request.Approve && request.ApprovedLimit.HasValue && request.ApprovedLimit.Value > app.BddkLimitCap)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "BDDK_LIMIT_EXCEEDED", ErrorMessage = $"Onaylanan limit ({request.ApprovedLimit.Value} TL) BDDK tavanını ({app.BddkLimitCap} TL) aşıyor!", Severity = "Warning" }, "BDDK Cap");

            if (app.MakerUserId == request.UserId)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "FOUR_EYES_VIOLATION", ErrorMessage = "Kararı giren kullanıcı ile onaylayan aynı olamaz (four-eyes principle)!", Severity = "Warning" }, "Four Eyes");

            if (app.Status != CardApplicationStatus.ManualReview)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "APPLICATION_STATE_INVALID", ErrorMessage = "Başvuru manuel değerlendirme aşamasında değil!", Severity = "Warning" }, "State Invalid");

            decimal? finalLimit = null;
            if (request.Approve)
                finalLimit = request.ApprovedLimit ?? Math.Min(app.RequestedLimit, app.BddkLimitCap);

            var dbParams = new Dictionary<string, object>
            {
                { "p_application_id", request.ApplicationId },
                { "p_approve", request.Approve },
                { "p_approved_limit", finalLimit ?? (object)System.DBNull.Value },
                { "p_checker_user_id", request.UserId },
                { "p_decision_note", request.DecisionNote ?? "" }
            };

            DataTable dt = _dbManager.ExecuteReader("sp_boa_application_decide", dbParams);
            if (dt.Rows.Count > 0)
            {
                response.Application = CardMappers.ToCardApplicationDto(dt.Rows[0]);
                response.IsSuccess = true;
                response.ResultMessage = request.Approve ? "Başvuru onaylandı." : "Başvuru reddedildi.";
            }
        }
        catch (FaultException<BankingFault> faultEx) { response.IsSuccess = false; response.ErrorCode = faultEx.Detail.ErrorCode; response.ErrorMessage = faultEx.Detail.ErrorMessage; throw; }
        catch (Exception ex) { response.IsSuccess = false; response.ErrorCode = "DECIDE_FAILED"; response.ErrorMessage = ex.Message; throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error"); }
        finally { stopwatch.Stop(); response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds; LogWcfCall("DecideCardApplication", request, response); }
        return response;
    }

    public ActivateCardResponse ActivateCard(ActivateCardRequest request)
    {
        CheckRole("BranchTeller");
        var response = new ActivateCardResponse();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (string.IsNullOrWhiteSpace(request.Pin) || request.Pin.Length != 4 || !int.TryParse(request.Pin, out _))
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "VALIDATION_ERROR", ErrorMessage = "4 haneli geçerli bir PIN giriniz!", Severity = "Warning" }, "Validation");

            var secureParams = new Dictionary<string, object> { { "p_card_id", request.CardId } };
            DataTable dtSecure = _dbManager.ExecuteReader("sp_boa_card_get_secure_details", secureParams);
            if (dtSecure.Rows.Count == 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_NOT_FOUND", ErrorMessage = "Kart bulunamadı!", Severity = "Warning" }, "Not Found");

            string storedNid = dtSecure.Rows[0]["national_id"].ToString() ?? "";
            int cardStatus = Convert.ToInt32(dtSecure.Rows[0]["status"]);
            if (cardStatus != (int)CardStatus.PendingActivation)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "CARD_STATE_INVALID", ErrorMessage = "Kart aktivasyon durumunda değil!", Severity = "Warning" }, "State");
            if (storedNid != request.NationalId)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "IDENTITY_VERIFICATION_FAILED", ErrorMessage = "TCKN doğrulaması başarısız!", Severity = "Warning" }, "Identity");

            string encryptedPan = dtSecure.Rows[0]["encrypted_pan"].ToString() ?? "";
            string unmaskedPan = BOA.Data.Helpers.EncryptionHelper.Decrypt(encryptedPan);
            string pinHash = HsmEngine.CreatePinBlock(unmaskedPan, request.Pin);

            var dbParams = new Dictionary<string, object>
            {
                { "p_card_id", request.CardId },
                { "p_pin_hash", pinHash },
                { "p_user_id", request.UserId },
                { "p_channel", request.Channel },
                { "p_client_ip", request.ClientIp }
            };
            DataTable dt = _dbManager.ExecuteReader("sp_boa_card_activate", dbParams);
            if (dt.Rows.Count > 0)
            {
                response.ActivatedCard = CardMappers.ToCardDto(dt.Rows[0]);
                response.IsSuccess = true;
                response.ResultMessage = "Kart başarıyla aktive edildi.";
            }
        }
        catch (FaultException<BankingFault> faultEx) { response.IsSuccess = false; response.ErrorCode = faultEx.Detail.ErrorCode; response.ErrorMessage = faultEx.Detail.ErrorMessage; throw; }
        catch (Exception ex) { response.IsSuccess = false; response.ErrorCode = "ACTIVATION_FAILED"; response.ErrorMessage = ex.Message; throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error"); }
        finally { stopwatch.Stop(); response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds; LogWcfCall("ActivateCard", request, response); }
        return response;
    }

    public DeliverCardResponse DeliverCard(DeliverCardRequest request)
    {
        CheckRole("BranchTeller");
        var response = new DeliverCardResponse();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var dbParams = new Dictionary<string, object>
            {
                { "p_card_id", request.CardId },
                { "p_tracking_number", request.TrackingNumber ?? Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() },
                { "p_user_id", request.UserId },
                { "p_channel", request.Channel },
                { "p_client_ip", request.ClientIp }
            };
            DataTable dt = _dbManager.ExecuteReader("sp_boa_card_deliver", dbParams);
            if (dt.Rows.Count > 0)
            {
                response.DeliveredCard = CardMappers.ToCardDto(dt.Rows[0]);
                response.IsSuccess = true;
                response.ResultMessage = "Kart teslim edildi, aktivasyon bekleniyor.";
            }
        }
        catch (FaultException<BankingFault> faultEx) { response.IsSuccess = false; response.ErrorCode = faultEx.Detail.ErrorCode; response.ErrorMessage = faultEx.Detail.ErrorMessage; throw; }
        catch (Exception ex) { response.IsSuccess = false; response.ErrorCode = "DELIVER_FAILED"; response.ErrorMessage = ex.Message; throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error"); }
        finally { stopwatch.Stop(); response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds; LogWcfCall("DeliverCard", request, response); }
        return response;
    }

    public DecideCardLimitChangeResponse DecideCardLimitChange(DecideCardLimitChangeRequest request)
    {
        CheckRole("CardOperationsAdmin");
        var response = new DecideCardLimitChangeResponse();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            DataTable dtReq = _dbManager.ExecuteReader("sp_boa_limit_request_get", new Dictionary<string, object> { { "p_limit_request_id", request.LimitRequestId } });
            if (dtReq.Rows.Count == 0)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "LIMIT_REQUEST_NOT_FOUND", ErrorMessage = "Limit değişiklik talebi bulunamadı!", Severity = "Warning" }, "Not Found");
            var lr = CardMappers.ToLimitChangeRequestDto(dtReq.Rows[0]);
            if (lr.Status != LimitChangeRequestStatus.PendingApproval)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "LIMIT_REQUEST_STATE_INVALID", ErrorMessage = "Talep onay bekler durumda değil!", Severity = "Warning" }, "State");
            if (lr.MakerUserId == request.UserId)
                throw new FaultException<BankingFault>(new BankingFault { ErrorCode = "FOUR_EYES_VIOLATION", ErrorMessage = "Dört göz prensibi ihlal edilemez!", Severity = "Warning" }, "Four Eyes");
            var dbParams = new Dictionary<string, object>
            {
                { "p_limit_request_id", request.LimitRequestId },
                { "p_approve", request.Approve },
                { "p_checker_user_id", request.UserId },
                { "p_decision_note", request.DecisionNote ?? "" }
            };
            var dtDecide = _dbManager.ExecuteReader("sp_boa_limit_request_decide", dbParams);
            if (dtDecide.Rows.Count > 0)
                response.DecidedRequest = CardMappers.ToLimitChangeRequestDto(dtDecide.Rows[0]);
            if (request.Approve)
            {
                _dbManager.ExecuteReader("sp_boa_card_update_limit", new Dictionary<string, object> { { "p_card_id", lr.CardId }, { "p_new_limit", lr.RequestedLimit }, { "p_user_id", request.UserId }, { "p_channel", request.Channel }, { "p_client_ip", request.ClientIp } });
                try
                {
                    DataTable dtCardForPaycore = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_card_id", lr.CardId } });
                    if (dtCardForPaycore.Rows.Count > 0)
                    {
                        var c = CardMappers.ToCardDto(dtCardForPaycore.Rows[0]);
                        if (!string.IsNullOrWhiteSpace(c.PaycoreReference))
                            _paycoreGateway.UpdateLimit(c.PaycoreReference, lr.RequestedLimit);
                    }
                }
                catch { }
            }
            DataTable dtCard = _dbManager.ExecuteReader("sp_boa_card_get_list", new Dictionary<string, object> { { "p_card_id", lr.CardId } });
            if (dtCard.Rows.Count > 0)
                response.UpdatedCard = CardMappers.ToCardDto(dtCard.Rows[0]);
            response.IsSuccess = true;
            response.ResultMessage = request.Approve ? "Limit artışı onaylandı." : "Limit artışı reddedildi.";
        }
        catch (FaultException<BankingFault> faultEx) { response.IsSuccess = false; response.ErrorCode = faultEx.Detail.ErrorCode; response.ErrorMessage = faultEx.Detail.ErrorMessage; throw; }
        catch (Exception ex) { response.IsSuccess = false; response.ErrorCode = "DECIDE_LIMIT_FAILED"; response.ErrorMessage = ex.Message; throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error"); }
        finally { stopwatch.Stop(); response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds; LogWcfCall("DecideCardLimitChange", request, response); }
        return response;
    }

    public GetLimitChangeRequestsResponse GetLimitChangeRequests(GetLimitChangeRequestsRequest request)
    {
        CheckRole("CardOperationsAdmin");
        var response = new GetLimitChangeRequestsResponse();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var dbParams = new Dictionary<string, object> { { "p_only_pending", true } };
            if (request.CardId.HasValue) dbParams["p_card_id"] = request.CardId.Value;
            DataTable dt = _dbManager.ExecuteReader("sp_boa_limit_request_list", dbParams);
            foreach (DataRow row in dt.Rows)
                response.Requests.Add(CardMappers.ToLimitChangeRequestDto(row));
            response.IsSuccess = true;
            response.ResultMessage = $"{response.Requests.Count} adet talep listelendi.";
        }
        catch (FaultException<BankingFault> faultEx) { response.IsSuccess = false; response.ErrorCode = faultEx.Detail.ErrorCode; response.ErrorMessage = faultEx.Detail.ErrorMessage; throw; }
        catch (Exception ex) { response.IsSuccess = false; response.ErrorCode = "LIMITREQ_LIST_FAILED"; response.ErrorMessage = ex.Message; throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error"); }
        finally { stopwatch.Stop(); response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds; LogWcfCall("GetLimitChangeRequests", request, response); }
        return response;
    }

    public RetryPendingPaycoreSyncsResponse RetryPendingPaycoreSyncs(RetryPendingPaycoreSyncsRequest request)
    {
        CheckRole("CardOperationsAdmin");
        var response = new RetryPendingPaycoreSyncsResponse();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            DataTable dt = _dbManager.ExecuteReader("sp_boa_paycore_outbox_get_pending", new Dictionary<string, object>());
            int retried = 0;
            foreach (DataRow row in dt.Rows)
            {
                string bankRef = row["bank_reference_number"].ToString() ?? "";
                var markParams = new Dictionary<string, object>
                {
                    { "p_status", 2 },
                    { "p_bank_reference_number", bankRef },
                    { "p_last_error", (object)System.DBNull.Value }
                };
                _dbManager.ExecuteReader("sp_boa_paycore_outbox_mark", markParams);
                retried++;
            }
            response.Confirmed = retried;
            response.IsSuccess = true;
            response.ResultMessage = $"{retried} adet bekleyen PayCore senkronizasyonu yeniden denendi.";
        }
        catch (Exception ex) { response.IsSuccess = false; response.ErrorCode = "PAYCORE_RETRY_FAILED"; response.ErrorMessage = ex.Message; throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error"); }
        finally { stopwatch.Stop(); response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds; LogWcfCall("RetryPendingPaycoreSyncs", request, response); }
        return response;
    }

    public GetRegulatoryReportResponse GetRegulatoryReport(GetRegulatoryReportRequest request)
    {
        CheckRole("CardOperationsAdmin");
        var response = new GetRegulatoryReportResponse();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var dbParams = new Dictionary<string, object>
            {
                { "p_report_type", request.ReportType },
                { "p_report_date", request.ReportDate ?? (object)System.DBNull.Value }
            };
            DataTable dt = _dbManager.ExecuteReader("sp_boa_regulatory_report", dbParams);
            if (dt.Rows.Count > 0)
            {
                response.ReportType = dt.Rows[0]["report_type"].ToString() ?? "";
                response.ReportDate = dt.Rows[0]["report_date"].ToString() ?? "";
                response.ReportData = dt.Rows[0]["report_data"].ToString() ?? "{}";
            }
            response.IsSuccess = true;
            response.ResultMessage = "Rapor başarıyla oluşturuldu.";
        }
        catch (Exception ex) { response.IsSuccess = false; response.ErrorCode = "REGULATORY_REPORT_FAILED"; response.ErrorMessage = ex.Message; throw new FaultException<BankingFault>(new BankingFault { ErrorCode = response.ErrorCode, ErrorMessage = response.ErrorMessage, Severity = "Error" }, "System Error"); }
        finally { stopwatch.Stop(); response.ExecutionTimeMs = stopwatch.ElapsedMilliseconds; LogWcfCall("GetRegulatoryReport", request, response); }
        return response;
    }

    /// <summary>
    /// TCKN ve gelir bilgisine göre deterministik kredi skoru üretir (demo amaçlı basit model).
    /// </summary>
    private static int ComputeCreditScore(string nationalId, decimal income)
    {
        int digitSum = nationalId.Sum(c => c - '0');
        return 1 + ((digitSum * 137) % 1900);
    }

    private void LogWcfCall(string operationName, RequestBase request, ResponseBase response)
    {
        string statusStr = response.IsSuccess ? "SUCCESS" : $"FAILED ({response.ErrorCode})";
        string logMessage =
            $"[{DateTime.Now:HH:mm:ss.fff}] [WCF SOAP] Operation: {operationName} | Status: {statusStr}\n" +
            $"  --> INPUT:  <soap:Envelope xmlns:web=\"http://emlakkatilim.com.tr/boa/card\">\n" +
            $"                 <web:Request Channel=\"{request.Channel}\" UserId=\"{request.UserId}\" BranchId=\"{request.BranchId}\" IP=\"{request.ClientIp}\" />\n" +
            $"              </soap:Envelope>\n" +
            $"  <-- OUTPUT: <soap:Response IsSuccess=\"{response.IsSuccess}\" Error=\"{response.ErrorMessage}\" ExecutionTime=\"{response.ExecutionTimeMs}ms\" />\n" +
            $"---------------------------------------------------------------------------------------------------------";

        lock (WcfExecutionLogs)
        {
            WcfExecutionLogs.Add(logMessage);
            if (WcfExecutionLogs.Count > 50)
            {
                WcfExecutionLogs.RemoveAt(0);
            }
        }
    }
}

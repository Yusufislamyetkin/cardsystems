using System;
using System.ServiceModel;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Requests;
using BOA.Common.Contracts.Enums;

namespace BOA.Client.ConsoleApp;

/// <summary>
/// İstemci tarafında (Client) WCF WSDL şemasına uygun olarak tanımlanan servis sözleşmesidir.
/// Sunucuda CoreWCF öznitelikleri kullanılırken, masaüstü/konsol istemcide WCF standardı olan 
/// 'System.ServiceModel' öznitelikleri kullanılır. Paylaşılan Request/Response modelleri üzerinden eşleşirler.
/// </summary>
[ServiceContract(Namespace = "http://emlakkatilim.com.tr/boa/card", Name = "ICardService")]
public interface ICardServiceClient
{
    [OperationContract]
    CreateCardResponse CreateCard(CreateCardRequest request);

    [OperationContract]
    GetCardListResponse GetCardList(GetCardListRequest request);

    [OperationContract]
    UpdateCardLimitResponse UpdateCardLimit(UpdateCardLimitRequest request);

    [OperationContract]
    SetCardStatusResponse SetCardStatus(SetCardStatusRequest request);

    [OperationContract]
    GetCardTransactionsResponse GetCardTransactions(GetCardTransactionsRequest request);

    [OperationContract]
    CreateTransactionResponse CreateTransaction(CreateTransactionRequest request);
}

class Program
{
    static void Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.WriteLine(" EMLAK KATILIM - BOA CORE BANKING PORTAL | WCF DESKTOP CLIENT     ");
        Console.WriteLine("==================================================================");
        Console.ResetColor();

        // 1. WCF Bağlantı Ayarlarının (Binding & Endpoint) Tanımlanması
        // Sunucunun Program.cs'te BasicHttpBinding ile /CardService.svc adresinden yayınladığı protokole bağlanıyoruz.
        var binding = new BasicHttpBinding();
        var endpoint = new EndpointAddress("http://localhost:5000/CardService.svc");

        // 2. WCF Kanal Fabrikası (Channel Factory) Oluşturulması
        // WCF mimarisinde proxy sınıfı üretmeden arayüz üzerinden doğrudan SOAP kanalı açmanın en temiz yoludur.
        var factory = new ChannelFactory<ICardServiceClient>(binding, endpoint);
        ICardServiceClient wcfClient = factory.CreateChannel();

        try
        {
            ExecuteListCards(wcfClient);
        }
        catch (Exception ex)
        {
            Console.WriteLine("STARTUP TEST ERROR: " + ex);
        }

        bool exit = false;
        while (!exit)
        {
            Console.WriteLine("\nLütfen Yapmak İstediğiniz İşlemi Seçin:");
            Console.WriteLine("1 - Tüm Kartları Listele (WCF SOAP Call)");
            Console.WriteLine("2 - Yeni Kart Tanımla (WCF SOAP Call)");
            Console.WriteLine("3 - Kart Bakiyesi Simüle Et (WCF SOAP Call)");
            Console.WriteLine("4 - Çıkış");
            Console.Write("Seçiminiz: ");
            
            string? choice = Console.ReadLine();
            try
            {
                switch (choice)
                {
                    case "1":
                        ExecuteListCards(wcfClient);
                        break;
                    case "2":
                        ExecuteCreateCard(wcfClient);
                        break;
                    case "3":
                        ExecuteSimulateTransaction(wcfClient);
                        break;
                    case "4":
                        exit = true;
                        break;
                    default:
                        Console.WriteLine("Geçersiz seçim!");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"WCF Servis Çağrısında Hata: {ex.Message}");
                Console.ResetColor();
            }
        }

        // Kanalı kapatıyoruz
        ((IClientChannel)wcfClient).Close();
        factory.Close();
    }

    /// <summary>
    /// WCF GetCardList metodunu tetikler ve dönen DTO listesini ekrana basar.
    /// </summary>
    static void ExecuteListCards(ICardServiceClient wcfClient)
    {
        Console.WriteLine("\n[WCF CALL] GetCardList çağrısı yapılıyor...");
        var request = new GetCardListRequest
        {
            Channel = "DESKTOP_CONSOLE",
            UserId = "SYSTEM_USER",
            BranchId = 101 // Ankara Şubesi
        };

        var response = wcfClient.GetCardList(request);

        if (response.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"WCF Çağrısı Başarılı! {response.ResultMessage}");
            Console.ResetColor();

            Console.WriteLine("------------------------------------------------------------------------------------------");
            Console.WriteLine($"{"ID",-4} | {"Kart Numarası",-19} | {"Kart Sahibi",-15} | {"Kart Tipi",-10} | {"Limit",-12} | {"Bakiye",-12} | {"Durum",-8}");
            Console.WriteLine("------------------------------------------------------------------------------------------");
            
            foreach (var card in response.Cards)
            {
                string typeStr = card.CardType == CardType.Debit ? "Banka" : "Kredi";
                string statusStr = card.Status == CardStatus.Active ? "Aktif" : card.Status == CardStatus.Blocked ? "Bloke" : "İptal";
                
                Console.WriteLine($"{card.CardId,-4} | {card.MaskedCardNumber,-19} | {card.CardHolderName,-15} | {typeStr,-10} | ₺{card.CardLimit,10:N2} | ₺{card.Balance,10:N2} | {statusStr,-8}");
            }
            Console.WriteLine("------------------------------------------------------------------------------------------");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Hata Kodu: {response.ErrorCode} | Mesaj: {response.ErrorMessage}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// WCF CreateCard metodunu tetikleyerek veritabanına yeni kart kaydeder.
    /// </summary>
    static void ExecuteCreateCard(ICardServiceClient wcfClient)
    {
        Console.WriteLine("\nYeni Kart Bilgileri:");
        Console.Write("Müşteri Adı Soyadı: ");
        string name = Console.ReadLine() ?? "ADSIZ";

        Console.Write("Müşteri T.C. Kimlik No: ");
        string nationalId = Console.ReadLine() ?? "";

        Console.Write("Kart Türü (1: Banka Kartı, 2: Kredi Kartı): ");
        string typeInput = Console.ReadLine() ?? "1";
        CardType type = typeInput == "2" ? CardType.Credit : CardType.Debit;

        decimal limit = 0;
        decimal balance = 0;

        if (type == CardType.Credit)
        {
            Console.Write("Kredi Kartı Limiti (₺): ");
            decimal.TryParse(Console.ReadLine(), out limit);
            balance = limit; // Kredi kartında bakiye kullanılabilir limit ile başlar
        }
        else
        {
            Console.Write("Başlangıç Hesap Bakiyesi (₺): ");
            decimal.TryParse(Console.ReadLine(), out balance);
        }

        Console.WriteLine("\n[WCF CALL] CreateCard çağrısı yapılıyor...");
        var request = new CreateCardRequest
        {
            CardHolderName = name.ToUpper(),
            NationalId = nationalId,
            CardType = type,
            Limit = limit,
            InitialBalance = balance,
            Channel = "DESKTOP_CONSOLE",
            UserId = "SYSTEM_USER",
            BranchId = 101
        };

        var response = wcfClient.CreateCard(request);

        if (response.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"WCF Çağrısı Başarılı! {response.ResultMessage}");
            Console.WriteLine($"Yeni Oluşturulan Kart ID: {response.CreatedCard.CardId}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Hata: {response.ErrorMessage}");
            Console.ResetColor();
        }
    }

    /// <summary>
    /// WCF CreateTransaction metodunu tetikleyerek işlem simüle eder.
    /// </summary>
    static void ExecuteSimulateTransaction(ICardServiceClient wcfClient)
    {
        Console.Write("\nİşlem Yapılacak Kart ID: ");
        if (!int.TryParse(Console.ReadLine(), out int cardId)) return;

        Console.Write("İşlem Türü (1: POS Harcaması, 2: ATM Çekim, 3: ATM Yatırma): ");
        string typeInput = Console.ReadLine() ?? "1";
        TransactionType type = typeInput == "3" ? TransactionType.Deposit : typeInput == "2" ? TransactionType.Withdrawal : TransactionType.Purchase;

        Console.Write("İşlem Tutarı (₺): ");
        decimal.TryParse(Console.ReadLine(), out decimal amount);

        Console.Write("İşlem Açıklaması: ");
        string desc = Console.ReadLine() ?? "Konsol Simülasyonu";

        Console.WriteLine("\n[WCF CALL] CreateTransaction çağrısı yapılıyor...");
        var request = new CreateTransactionRequest
        {
            CardId = cardId,
            TransactionType = type,
            Amount = amount,
            Description = desc,
            Channel = "DESKTOP_CONSOLE",
            UserId = "SYSTEM_USER"
        };

        var response = wcfClient.CreateTransaction(request);

        if (response.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"WCF Çağrısı Başarılı! {response.ResultMessage}");
            Console.WriteLine($"İşlem Sonrası Güncel Bakiye: ₺{response.UpdatedCard.Balance:N2}");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Hata: {response.ErrorMessage}");
            Console.ResetColor();
        }
    }
}

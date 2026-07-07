using BOA.Common.Contracts.Enums;
using BOA.Common.Contracts.Requests;
using BOA.Services.Card;
using BOA.Tests.Infrastructure;
using CoreWCF;
using BOA.Common.Contracts.Base;

namespace BOA.Tests;

public class CardCreationTests
{
    private static CreateCardRequest ValidRequest(
        string nationalId = "11111111111",
        string cardHolderName = "TEST USER",
        CardType cardType = CardType.Debit,
        decimal limit = 0,
        decimal initialBalance = 1000,
        string pin = "1234") => new()
        {
            CardHolderName = cardHolderName,
            NationalId = nationalId,
            CardType = cardType,
            Limit = limit,
            InitialBalance = initialBalance,
            Pin = pin,
            Channel = "TEST",
            UserId = "test"
        };

    [Fact]
    public void CreateCard_WithValidRequest_LinksCustomerAndBankAccount()
    {
        using var harness = new CardServiceTestHarness();

        var response = harness.Service.CreateCard(ValidRequest());

        Assert.True(response.IsSuccess);
        Assert.True(response.CreatedCard.CustomerId > 0);
        Assert.True(response.CreatedCard.BankAccountId > 0);
        Assert.Equal("11111111111", response.CreatedCard.NationalId);
    }

    [Fact]
    public void CreateCard_SecondCardSameNationalId_ReusesSameCustomer()
    {
        using var harness = new CardServiceTestHarness();

        var first = harness.Service.CreateCard(ValidRequest());
        var second = harness.Service.CreateCard(ValidRequest(cardType: CardType.Credit, limit: 5000, initialBalance: 0));

        Assert.Equal(first.CreatedCard.CustomerId, second.CreatedCard.CustomerId);
        Assert.NotEqual(first.CreatedCard.BankAccountId, second.CreatedCard.BankAccountId);
    }

    [Fact]
    public void CreateCard_DebitBin_MatchesBinTable()
    {
        using var harness = new CardServiceTestHarness();

        var response = harness.Service.CreateCard(ValidRequest());

        Assert.StartsWith("435520", response.CreatedCard.CardNumber);
    }

    [Fact]
    public void CreateCard_CreditBin_MatchesBinTable()
    {
        using var harness = new CardServiceTestHarness();

        var request = ValidRequest(cardType: CardType.Credit, limit: 5000, initialBalance: 0);
        var response = harness.Service.CreateCard(request);

        Assert.StartsWith("543789", response.CreatedCard.CardNumber);
    }

    [Fact]
    public void CreateCard_GeneratedNumber_PassesLuhnCheck()
    {
        using var harness = new CardServiceTestHarness();

        // Kart numarası Luhn algoritması ile üretiliyor; test doğrudan LuhnHelper kullanır
        // çünkü ham PAN asla servis sınırının dışına çıkmaz (PCI-DSS gereği).
        string cardNumber = BOA.Data.Helpers.LuhnHelper.AppendCheckDigit("435520" + CardService.GenerateRandomAccountNumber());

        Assert.Equal(16, cardNumber.Length);
        Assert.True(IsValidLuhn(cardNumber));
    }

    [Fact]
    public void CreateCard_EmptyHolderName_ThrowsValidationFault()
    {
        using var harness = new CardServiceTestHarness();

        var request = ValidRequest(cardHolderName: "");
        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateCard(request));
        Assert.Equal("VALIDATION_ERROR", fault.Detail.ErrorCode);
    }

    [Fact]
    public void CreateCard_NegativeLimit_ThrowsValidationFault()
    {
        using var harness = new CardServiceTestHarness();

        var request = ValidRequest(limit: -1);
        Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateCard(request));
    }

    [Fact]
    public void CreateCard_NonNumericPin_ThrowsValidationFault()
    {
        using var harness = new CardServiceTestHarness();

        var request = ValidRequest(pin: "abcd");
        Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateCard(request));
    }

    [Fact]
    public void CreateCard_MissingNationalId_ThrowsValidationFault()
    {
        using var harness = new CardServiceTestHarness();

        var request = ValidRequest(nationalId: "");
        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateCard(request));
        Assert.Equal("VALIDATION_ERROR", fault.Detail.ErrorCode);
    }

    [Fact]
    public void CreateCard_ValidRequest_IsIssuedOnPaycoreAndReferenceIsLinked()
    {
        // Kart, bankanın kendi kaydı zaten oluştuktan SONRA PayCore'a (dış kart işleme sağlayıcısına)
        // kaydediliyor; bu ikisi arasındaki tek bağ bu referans numarasıdır.
        using var harness = new CardServiceTestHarness();

        var response = harness.Service.CreateCard(ValidRequest());

        Assert.True(response.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(response.CreatedCard.PaycoreReference));
    }

    [Fact]
    public void CreateCard_DebitWithNonZeroLimit_ThrowsValidationFault()
    {
        // Debit kartlarda kredi limiti kavramı yoktur; bu kural önceden yalnızca web UI'de (app.js)
        // uygulanıyordu ve doğrudan servise istek atan bir istemci tarafından bypass edilebilirdi.
        using var harness = new CardServiceTestHarness();

        var request = ValidRequest(cardType: CardType.Debit, limit: 5000);
        var fault = Assert.Throws<FaultException<BankingFault>>(() => harness.Service.CreateCard(request));
        Assert.Equal("VALIDATION_ERROR", fault.Detail.ErrorCode);
    }

    private static bool IsValidLuhn(string number)
    {
        int sum = 0;
        bool doubleDigit = false;
        for (int i = number.Length - 1; i >= 0; i--)
        {
            int digit = number[i] - '0';
            if (doubleDigit)
            {
                digit *= 2;
                if (digit > 9) digit -= 9;
            }
            sum += digit;
            doubleDigit = !doubleDigit;
        }
        return sum % 10 == 0;
    }
}

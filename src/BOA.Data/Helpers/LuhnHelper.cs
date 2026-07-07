using System;
using System.Text;

namespace BOA.Data.Helpers;

/// <summary>
/// ISO/IEC 7812-1 standardına uygun Luhn (Mod 10) algoritması.
/// Kart numaralarının geçerlilik kontrolü ve check digit hesaplaması için kullanılır.
/// Gerçek banka kartı numarası üretim sürecinin temel bir bileşenidir.
/// </summary>
public static class LuhnHelper
{
    /// <summary>
    /// Verilen bir kart numarasının (son hane check digit) Luhn algoritmasına göre
    /// geçerli olup olmadığını kontrol eder.
    /// </summary>
    /// <param name="cardNumber">16 haneli kart numarası (sadece rakam)</param>
    /// <returns>Geçerli ise true, değilse false</returns>
    public static bool IsValid(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber))
            return false;

        int sum = 0;
        bool alternate = false;

        // Sağdan sola doğru Luhn hesaplaması
        for (int i = cardNumber.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(cardNumber[i]))
                return false;

            int digit = cardNumber[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    /// <summary>
    /// İlk 15 hanesi (veya herhangi bir sayıda ilk hane) verilen bir BIN + seri numarası
    /// için Luhn check digit'ini hesaplar. Sonuç, tam 16 haneli geçerli bir kart numarasıdır.
    /// </summary>
    /// <param name="partialPan">İlk N hanesi (örneğin BIN + 9 haneli seri = 15 hane)</param>
    /// <returns>Check digit eklenmiş tam kart numarası</returns>
    public static string AppendCheckDigit(string partialPan)
    {
        if (string.IsNullOrEmpty(partialPan))
            throw new ArgumentException("Kısmi kart numarası boş olamaz.", nameof(partialPan));

        int checkDigit = CalculateCheckDigit(partialPan);
        return partialPan + checkDigit.ToString();
    }

    /// <summary>
    /// Kısmi bir kart numarası için Luhn check digit'ini (0-9) hesaplar.
    /// PartialPan'in sonuna eklenecek tek haneyi döndürür.
    /// </summary>
    public static int CalculateCheckDigit(string partialPan)
    {
        // Check digit konumuna 0 koyarak Luhn toplamını hesapla
        int sum = 0;
        bool alternate = true; // Check digit konumu (en sağ) alternate = 1 (double)

        for (int i = partialPan.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(partialPan[i]))
                throw new ArgumentException($"Kart numarası sadece rakamlardan oluşmalıdır. Hatalı karakter: '{partialPan[i]}'", nameof(partialPan));

            int digit = partialPan[i] - '0';

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        // Toplamı 10'un katına tamamlayan değer check digit'tir
        int checkDigit = (10 - (sum % 10)) % 10;
        return checkDigit;
    }

    /// <summary>
    /// Kart numarasından sadece rakamları ayıklar (boşluk, tire gibi karakterleri temizler).
    /// </summary>
    public static string Sanitize(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber))
            return string.Empty;

        var sb = new StringBuilder(cardNumber.Length);
        foreach (char c in cardNumber)
        {
            if (char.IsDigit(c))
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Kart numarasını PCI-DSS uyumlu şekilde maskeler.
    /// Örnek: "4355123456789012" → "435512******9012" veya "4355 12** **** 9012"
    /// </summary>
    /// <param name="cardNumber">Tam kart numarası</param>
    /// <param name="formatted">Boşluklu format kullanılsın mı (4-4-4-4 bloklar)</param>
    /// <returns>Maskeli kart numarası</returns>
    public static string Mask(string cardNumber, bool formatted = true)
    {
        string sanitized = Sanitize(cardNumber);
        if (sanitized.Length < 10)
            return sanitized; // Çok kısa, maskeleme anlamsız

        string first6 = sanitized.Substring(0, 6);
        string last4 = sanitized.Substring(sanitized.Length - 4);

        if (formatted)
            return $"{first6.Substring(0, 4)} {first6.Substring(4, 2)}** **** {last4}";

        return $"{first6}{new string('*', sanitized.Length - 10)}{last4}";
    }
}
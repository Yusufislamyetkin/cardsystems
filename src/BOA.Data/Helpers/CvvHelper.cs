using System;
using System.Security.Cryptography;
using System.Text;

namespace BOA.Data.Helpers;

/// <summary>
/// CVV (Card Verification Value) ve CVV2 (Card Verification Value 2) üretimi için
/// yardımcı sınıf. Gerçek bankacılıkta CVV, 3DES anahtarları ile PAN, expiry ve
/// service code kullanılarak üretilir. Bu implementasyon, Visa CVV metodu ve
/// Mastercard CVC/CVC2 metodolojisine uygun basitleştirilmiş bir simülasyondur.
/// 
/// Gerçek üretim ortamında CVV değerleri HSM içinde hesaplanır ve saklanmaz.
/// Bu eğitim/demo implementasyonunda AES tabanlı deterministik bir türetme kullanılır.
/// </summary>
public static class CvvHelper
{
    /// <summary>CVV anahtar türetme için kullanılan sabit salt (gerçekte HSM içindedir).</summary>
    private static readonly byte[] CvvKeySalt = Encoding.ASCII.GetBytes("BOA_CVV_2024_SALT_KEY_V1");

    /// <summary>CVV2 anahtar türetme için kullanılan sabit salt.</summary>
    private static readonly byte[] Cvv2KeySalt = Encoding.ASCII.GetBytes("BOA_CVV2_2024_SALT_KEY_V1");

    /// <summary>
    /// CVV (manyetik şerit için Card Verification Value) üretir.
    /// Gerçek bankacılıkta: 3DES(PAN + ExpiryYYMM + ServiceCode) ile hesaplanır.
    /// </summary>
    /// <param name="pan">Açık (unmasked) kart numarası (16 hane)</param>
    /// <param name="expiryYYMM">Son kullanma tarihi YYMM formatında (örn: "2806")</param>
    /// <param name="serviceCode">EMV Service Code (örn: "201" — Chip + PIN + Normal authorization)</param>
    /// <returns>3 haneli CVV kodu</returns>
    public static string GenerateCvv(string pan, string expiryYYMM, string serviceCode)
    {
        // Visa/Mastercard CVV metodu: PAN + Expiry + Service Code birleştirilir
        string cvvData = SanitizePan(pan) + expiryYYMM + serviceCode;
        return Derive3DigitCode(cvvData, CvvKeySalt, "CVV");
    }

    /// <summary>
    /// CVV2 (kartın arkasındaki 3 haneli güvenlik kodu) üretir.
    /// CVV'den farklı bir anahtar türevleme kullanır; aynı hesaplama metodolojisine sahiptir.
    /// </summary>
    /// <param name="pan">Açık (unmasked) kart numarası (16 hane)</param>
    /// <param name="expiryYYMM">Son kullanma tarihi YYMM formatında</param>
    /// <param name="serviceCode">EMV Service Code (CVV2 için genelde "000" kullanılır)</param>
    /// <returns>3 haneli CVV2 kodu</returns>
    public static string GenerateCvv2(string pan, string expiryYYMM, string serviceCode)
    {
        string cvvData = SanitizePan(pan) + expiryYYMM + serviceCode;
        return Derive3DigitCode(cvvData, Cvv2KeySalt, "CVV2");
    }

    /// <summary>
    /// iCVV (dinamik CVV — temassız işlemler için) üretir. iCVV, CVV'den farklı bir
    /// anahtar ve farklı bir Service Code ("999") ile üretilir.
    /// </summary>
    public static string GenerateICvv(string pan, string expiryYYMM)
    {
        string cvvData = SanitizePan(pan) + expiryYYMM + "999";
        return Derive3DigitCode(cvvData, CvvKeySalt, "iCVV");
    }

    /// <summary>
    /// Kart numarası ve son kullanma tarihine göre CVV2 değerini kolayca üretir.
    /// Standart Service Code "000" kullanır.
    /// </summary>
    public static string GenerateCvv2(string pan, DateTime expiryDate)
    {
        string expiryYYMM = expiryDate.ToString("yyMM");
        return GenerateCvv2(pan, expiryYYMM, "000");
    }

    /// <summary>
    /// Girdi verisinden deterministik olarak 3 haneli bir sayısal kod türetir.
    /// Gerçek CVV hesaplaması 3DES kullanır; burada AES-256 + HMAC ile simüle edilir.
    /// </summary>
    private static string Derive3DigitCode(string input, byte[] salt, string label)
    {
        byte[] inputBytes = Encoding.ASCII.GetBytes(input);
        byte[] combined = new byte[salt.Length + inputBytes.Length];
        Buffer.BlockCopy(salt, 0, combined, 0, salt.Length);
        Buffer.BlockCopy(inputBytes, 0, combined, salt.Length, inputBytes.Length);

        // Etiket bilgisini de hash'e dahil et (key separation)
        byte[] labelBytes = Encoding.ASCII.GetBytes(label);
        using var hmac = new HMACSHA256(combined);
        byte[] hash = hmac.ComputeHash(labelBytes);

        // Hash'in ilk 3 byte'ını al ve 0-999 aralığında bir sayıya dönüştür
        int value = ((hash[0] & 0x7F) << 16) | (hash[1] << 8) | hash[2];
        int cvv = value % 1000;

        return cvv.ToString("D3"); // Her zaman 3 haneli (001-999)
    }

    /// <summary>
    /// PAN'dan sadece rakamları alır, boşlukları ve özel karakterleri temizler.
    /// </summary>
    private static string SanitizePan(string pan)
    {
        if (string.IsNullOrEmpty(pan))
            throw new ArgumentException("PAN boş olamaz.", nameof(pan));

        var sb = new StringBuilder(pan.Length);
        foreach (char c in pan)
        {
            if (char.IsDigit(c))
                sb.Append(c);
        }

        string result = sb.ToString();
        if (result.Length < 12)
            throw new ArgumentException($"PAN en az 12 haneli olmalıdır. Geçersiz uzunluk: {result.Length}", nameof(pan));

        return result;
    }

    /// <summary>
    /// CVV değerinin 3 haneli (000-999) olup olmadığını kontrol eder.
    /// </summary>
    public static bool IsValidCvvFormat(string cvv)
    {
        return !string.IsNullOrEmpty(cvv)
               && cvv.Length == 3
               && int.TryParse(cvv, out _);
    }
}
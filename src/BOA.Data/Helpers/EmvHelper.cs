using System;
using System.Globalization;
using System.Text;

namespace BOA.Data.Helpers;

/// <summary>
/// EMV (Europay, Mastercard, Visa) kart veri elemanlarını üreten yardımcı sınıf.
/// Track2 eşdeğer verisi, Service Code, EMBOSS isim formatlama ve
/// kart son kullanma tarihi hesaplama işlemlerini içerir.
/// </summary>
public static class EmvHelper
{
    // BDDK ve bankacılık sektör standartlarına göre vade süreleri
    private const int DebitCardValidityYears = 5;   // Banka kartı: 5 yıl
    private const int CreditCardValidityYears = 3;  // Kredi kartı: 3 yıl

    /// <summary>
    /// Kart türüne göre son kullanma tarihini hesaplar.
    /// Banka kartı (Debit) → 5 yıl, Kredi kartı (Credit) → 3 yıl.
    /// Vade tarihi her zaman ayın son günü olarak belirlenir.
    /// </summary>
    /// <param name="isCreditCard">Kredi kartı ise true, banka kartı ise false</param>
    /// <returns>Ayın son gününe normalize edilmiş son kullanma tarihi</returns>
    public static DateTime CalculateExpiryDate(bool isCreditCard)
    {
        int years = isCreditCard ? CreditCardValidityYears : DebitCardValidityYears;
        DateTime expiryDate = DateTime.Now.AddYears(years);

        // Ayın son gününe normalize et (Türkiye bankacılık standardı)
        int lastDayOfMonth = DateTime.DaysInMonth(expiryDate.Year, expiryDate.Month);
        return new DateTime(expiryDate.Year, expiryDate.Month, lastDayOfMonth);
    }

    /// <summary>
    /// Son kullanma tarihini YYMM formatında döndürür (EMV standardı).
    /// Örn: 31 Aralık 2028 → "2812"
    /// </summary>
    public static string ToExpiryYYMM(DateTime expiryDate)
    {
        return expiryDate.ToString("yyMM");
    }

    /// <summary>
    /// Son kullanma tarihini MM/YY formatında döndürür (kart ön yüzü).
    /// Örn: 31 Aralık 2028 → "12/28"
    /// </summary>
    public static string ToExpiryDisplay(DateTime expiryDate)
    {
        return expiryDate.ToString("MM/yy");
    }

    /// <summary>
    /// EMV Service Code üretir. Service Code, kartın kullanım kısıtlamalarını
    /// belirten 3 haneli bir koddur.
    /// 
    /// 1. Hane (Interchange):
    ///   1 — International interchange (yurt dışı kullanıma açık)
    ///   2 — International interchange (chip ile)
    ///   5 — National only (sadece yurt içi)
    ///   6 — National only (chip ile)
    /// 
    /// 2. Hane (Authorization Processing):
    ///   0 — Normal (çevrimiçi)
    ///   1 — Chip ile (online PIN doğrulaması)
    ///   2 — Chip ile (imza veya offline PIN)
    /// 
    /// 3. Hane (Services & PIN Requirements):
    ///   0 — PIN gerektirmez
    ///   1 — PIN gerektirir
    ///   2 — PIN gerektirmez (chip ile)
    /// </summary>
    /// <param name="hasChip">Chip kart ise true</param>
    /// <param name="international">Yurt dışı kullanıma açık ise true</param>
    /// <returns>3 haneli EMV Service Code</returns>
    public static string GenerateServiceCode(bool hasChip = true, bool international = true)
    {
        // Chip'li, uluslararası, PIN doğrulamalı: 201 (en yaygın kombinasyon)
        if (hasChip && international)
            return "201";  // Chip + International, normal authorization, PIN required

        if (hasChip && !international)
            return "601";  // Chip + National only, normal authorization, PIN required

        if (!hasChip && international)
            return "101";  // Magnetic stripe, International, PIN required

        return "501";       // Magnetic stripe, National only, PIN required
    }

    /// <summary>
    /// Track2 eşdeğer verisini (manyetik şerit Track2 formatı) üretir.
    /// Format: PAN=YYMMServiceCode... (start sentinel % ve end sentinel ? hariç)
    /// 
    /// Gerçek Track2 verisi: %4355123456789012^DOE/JOHN^28122010000000000000? 
    /// Burada sadece ödeme sistemlerinin kullandığı PAN+Expiry+ServiceCode kısmı üretilir.
    /// </summary>
    /// <param name="pan">16 haneli kart numarası</param>
    /// <param name="expiryYYMM">YYMM formatında son kullanma tarihi</param>
    /// <param name="serviceCode">3 haneli EMV Service Code</param>
    /// <returns>Track2 eşdeğer verisi</returns>
    public static string GenerateTrack2Data(string pan, string expiryYYMM, string serviceCode)
    {
        string sanitizedPan = LuhnHelper.Sanitize(pan);
        if (sanitizedPan.Length < 13 || sanitizedPan.Length > 19)
            throw new ArgumentException($"Geçersiz PAN uzunluğu: {sanitizedPan.Length}", nameof(pan));

        // Track2 formatı: PAN + 'D' separator + Expiry YYMM + Service Code + opsiyonel PVKI + CVV + padding
        return $"{sanitizedPan}={expiryYYMM}{serviceCode}00000000000000";
    }

    /// <summary>
    /// Kart sahibi adını bankacılık standartlarına uygun şekilde EMBOSS formatına dönüştürür.
    /// - Maksimum 21 karakter (kart basım limiti)
    /// - Tamamı büyük harf (uppercase)
    /// - Türkçe karakterler ASCII eşdeğerlerine dönüştürülür: İ→I, Ş→S, Ğ→G, Ç→C, Ü→U, Ö→O
    /// - Soyad virgülle ayrılır: "SOYAD AD" formatı (bazı bankalar)
    /// - Fazla karakterler kısaltılır
    /// </summary>
    /// <param name="fullName">Kart sahibi tam adı</param>
    /// <returns>EMBOSS formatında isim (max 21 karakter, uppercase)</returns>
    public static string FormatEmbossName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return string.Empty;

        string normalized = NormalizeTurkishCharacters(fullName.ToUpperInvariant().Trim());

        // Fazladan boşlukları temizle
        var sb = new StringBuilder(normalized.Length);
        bool lastWasSpace = false;
        foreach (char c in normalized)
        {
            if (c == ' ')
            {
                if (!lastWasSpace)
                    sb.Append(c);
                lastWasSpace = true;
            }
            else if (c >= 'A' && c <= 'Z' || c == '.' || c == '-' || c == '\'')
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }

        string result = sb.ToString().Trim();

        // Maksimum 21 karakter sınırlaması
        if (result.Length > 21)
        {
            // Soyadın ilk 17 karakteri + boşluk + adın ilk harfi nokta
            string[] parts = result.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                string lastName = parts[0]; // Soyad (ilk kısım)
                string firstName = parts[1]; // Ad
                if (lastName.Length > 17)
                    lastName = lastName.Substring(0, 17);
                result = $"{lastName} {firstName[0]}.";
            }

            // Hala 21 karakterden uzunsa kes
            if (result.Length > 21)
                result = result.Substring(0, 21);
        }

        return result;
    }

    /// <summary>
    /// Türkçe karakterleri ASCII eşdeğerlerine dönüştürür.
    /// </summary>
    private static string NormalizeTurkishCharacters(string input)
    {
        return input
            .Replace('İ', 'I')
            .Replace('I', 'I')  // Türkçe I zaten I
            .Replace('Ş', 'S')
            .Replace('Ğ', 'G')
            .Replace('Ç', 'C')
            .Replace('Ü', 'U')
            .Replace('Ö', 'O')
            .Replace('ı', 'I')  // lowercase dotless i → I
            .Replace('ş', 'S')
            .Replace('ğ', 'G')
            .Replace('ç', 'C')
            .Replace('ü', 'U')
            .Replace('ö', 'O');
    }

    /// <summary>
    /// Kart tipi ve ürün segmentine göre kartın limit tavanını belirler.
    /// BDDK düzenlemelerine göre kredi kartı limitleri gelirle ilişkilidir;
    /// bu tavanlar bankanın iç politikasına göre belirlenir.
    /// </summary>
    /// <param name="cardProduct">Kart ürün segmenti</param>
    /// <returns>Ürün segmentine göre maksimum limit</returns>
    public static decimal GetProductLimitCap(BOA.Common.Contracts.Enums.CardProduct cardProduct)
    {
        return cardProduct switch
        {
            BOA.Common.Contracts.Enums.CardProduct.Classic => 25000m,
            BOA.Common.Contracts.Enums.CardProduct.Gold => 100000m,
            BOA.Common.Contracts.Enums.CardProduct.Platinum => 250000m,
            BOA.Common.Contracts.Enums.CardProduct.Business => 500000m,
            BOA.Common.Contracts.Enums.CardProduct.Premium => 1000000m,
            _ => 25000m
        };
    }

    /// <summary>
    /// BIN numarasına göre kart markasını belirler.
    /// </summary>
    /// <param name="bin">6 haneli BIN kodu</param>
    /// <returns>Kart markası</returns>
    public static BOA.Common.Contracts.Enums.CardBrand DetectCardBrand(string bin)
    {
        if (string.IsNullOrEmpty(bin) || bin.Length < 1)
            return BOA.Common.Contracts.Enums.CardBrand.Troy;

        char firstDigit = bin[0];

        return firstDigit switch
        {
            '4' => BOA.Common.Contracts.Enums.CardBrand.Visa,
            '5' => BOA.Common.Contracts.Enums.CardBrand.Mastercard,
            '2' => bin.Length >= 4 && int.Parse(bin.Substring(0, 4)) >= 2221 && int.Parse(bin.Substring(0, 4)) <= 2720
                ? BOA.Common.Contracts.Enums.CardBrand.Mastercard
                : BOA.Common.Contracts.Enums.CardBrand.Troy,
            '3' => bin.Length >= 2 && (bin[1] == '4' || bin[1] == '7')
                ? BOA.Common.Contracts.Enums.CardBrand.AmericanExpress
                : BOA.Common.Contracts.Enums.CardBrand.Troy,
            '9' => BOA.Common.Contracts.Enums.CardBrand.Troy, // Troy BIN aralığı (yerli kart)
            _ => BOA.Common.Contracts.Enums.CardBrand.Troy
        };
    }
}
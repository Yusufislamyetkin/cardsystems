using System;
using System.Text;

namespace BOA.Services.Card.Hsm;

/// <summary>
/// Bankacılık standartlarında (ISO 9564 Format 0) çalışan Donanımsal Güvenlik Modülü (HSM) Simülatörüdür.
/// Kart şifrelerinin (PIN) güvenli bir şekilde ağda taşınmasını ve doğrulanmasını sağlar.
/// </summary>
public static class HsmEngine
{
    // HSM Master Key (LMK) simülasyonu
    private static readonly string HsmMasterKey = "EMLAK_LMK_MASTER_KEY_2026";

    /// <summary>
    /// ISO 9564 Format 0 standardında PIN Block üretir.
    /// PIN Block = PIN_Part XOR PAN_Part
    /// </summary>
    public static string CreatePinBlock(string pan, string pin)
    {
        if (pin.Length < 4 || pin.Length > 12)
            throw new ArgumentException("PIN uzunluğu 4 ile 12 hane arasında olmalıdır!");

        // 1. PIN Part Hazırlama: 0 + L + PIN + F padding (16 hane hex)
        string pinPart = $"0{pin.Length}{pin}".PadRight(16, 'F');

        // 2. PAN Part Hazırlama: 4 adet '0' + PAN'ın son hanesi (Luhn check) hariç en sağdaki 12 hanesi
        string cleanPan = pan.Replace(" ", "").Trim();
        if (cleanPan.Length < 13)
            throw new ArgumentException("Geçersiz PAN uzunluğu!");

        string panPart = "0000" + cleanPan.Substring(cleanPan.Length - 13, 12);

        // 3. Hex XOR İşlemi
        byte[] pinBytes = HexStringToByteArray(pinPart);
        byte[] panBytes = HexStringToByteArray(panPart);
        byte[] pinBlockBytes = new byte[8];

        for (int i = 0; i < 8; i++)
        {
            pinBlockBytes[i] = (byte)(pinBytes[i] ^ panBytes[i]);
        }

        return ByteArrayToHexString(pinBlockBytes).ToUpperInvariant();
    }

    /// <summary>
    /// Gelen PIN Block ve PAN kullanarak şifrenin doğruluğunu kontrol eder.
    /// </summary>
    public static bool VerifyPinBlock(string pan, string pinBlock, string expectedPin)
    {
        try
        {
            // Beklenen PIN Block üretilir ve gelenle karşılaştırılır
            string expectedPinBlock = CreatePinBlock(pan, expectedPin);
            return string.Equals(pinBlock, expectedPinBlock, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// PIN Block'u çözerek içindeki ham PIN şifresini çıkarır (Sadece HSM içerisinde gerçekleşir).
    /// </summary>
    public static string DecryptPinBlock(string pan, string pinBlock)
    {
        string cleanPan = pan.Replace(" ", "").Trim();
        string panPart = "0000" + cleanPan.Substring(cleanPan.Length - 13, 12);

        byte[] blockBytes = HexStringToByteArray(pinBlock);
        byte[] panBytes = HexStringToByteArray(panPart);
        byte[] pinBytes = new byte[8];

        for (int i = 0; i < 8; i++)
        {
            pinBytes[i] = (byte)(blockBytes[i] ^ panBytes[i]);
        }

        string pinPart = ByteArrayToHexString(pinBytes);
        int pinLen = int.Parse(pinPart.Substring(1, 1));
        return pinPart.Substring(2, pinLen);
    }

    // Helper: Hex string -> byte array
    private static byte[] HexStringToByteArray(string hex)
    {
        byte[] arr = new byte[hex.Length / 2];
        for (int i = 0; i < arr.Length; i++)
        {
            arr[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }
        return arr;
    }

    // Helper: byte array -> Hex string
    private static string ByteArrayToHexString(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
        {
            sb.Append(b.ToString("x2"));
        }
        return sb.ToString();
    }
}

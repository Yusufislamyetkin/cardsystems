using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace BOA.Data.Helpers;

/// <summary>
/// PCI-DSS uyumluluğu için kart numaralarını (PAN) ve hassas verileri şifreleyen/çözen yardımcı sınıftır.
/// AES-256 simetrik şifreleme algoritmasını kullanır.
/// </summary>
public static class EncryptionHelper
{
    // Anahtar, kaynak kodda tutulmaz; süreç başlangıcında Initialize(...) ile (config/KMS/HSM'den) enjekte edilir.
    private static byte[]? _key;

    /// <summary>
    /// Uygulama başlangıcında (Program.cs) yapılandırma/KMS'den okunan 256-bit anahtarı ayarlar.
    /// </summary>
    public static void Initialize(byte[] key)
    {
        if (key == null || key.Length != 32)
            throw new ArgumentException("AES-256 anahtarı 32 byte (256-bit) uzunluğunda olmalıdır.", nameof(key));

        _key = key;
    }

    private static byte[] GetKeyOrThrow()
    {
        return _key ?? throw new InvalidOperationException(
            "EncryptionHelper.Initialize(key) çağrılmadan şifreleme/çözme yapılamaz. Anahtar yapılandırmadan (EncryptionSettings:Key) yüklenmelidir.");
    }

    /// <summary>
    /// Gelen düz metni AES-256 ile şifreler. Her çağrıda rastgele üretilen IV, ciphertext'in başına eklenerek
    /// Base64 formatında döner (deterministic/aynı-ciphertext üretimini engellemek için).
    /// </summary>
    public static string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;

        using var aes = Aes.Create();
        aes.Key = GetKeyOrThrow();
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        using var ms = new MemoryStream();
        ms.Write(aes.IV, 0, aes.IV.Length);
        using (var cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
        {
            using (var sw = new StreamWriter(cs))
            {
                sw.Write(plainText);
            }
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Base64 formatındaki şifreli metni (baştaki IV'yi ayırarak) AES-256 ile çözer.
    /// </summary>
    public static string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return string.Empty;

        try
        {
            var fullBytes = Convert.FromBase64String(cipherText);

            using var aes = Aes.Create();
            aes.Key = GetKeyOrThrow();

            var iv = new byte[16];
            Array.Copy(fullBytes, 0, iv, 0, iv.Length);
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            using var ms = new MemoryStream(fullBytes, iv.Length, fullBytes.Length - iv.Length);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs);

            return sr.ReadToEnd();
        }
        catch
        {
            return "DECRYPTION_ERROR";
        }
    }
}

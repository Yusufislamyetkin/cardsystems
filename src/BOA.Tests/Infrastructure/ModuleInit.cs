using System.Runtime.CompilerServices;
using BOA.Data.Helpers;

namespace BOA.Tests.Infrastructure;

/// <summary>
/// EncryptionHelper statik bir anahtar durumu tutar ve Initialize çağrılmadan Encrypt/Decrypt
/// kullanılamaz (bkz. Program.cs'teki üretim kurulumu). Test derlemesi yüklendiğinde bir kere
/// çağrılarak tüm test sınıflarının aynı (sabit, yalnızca test amaçlı) anahtarı paylaşması sağlanır.
/// </summary>
internal static class ModuleInit
{
    [ModuleInitializer]
    public static void Initialize()
    {
        // Yalnızca testler için sabit bir 32 byte'lık anahtar. Üretimde bu şekilde asla sabit kodlanmaz
        // (bkz. BOA.App/Program.cs — BOA_ENCRYPTION_KEY ortam değişkeni / appsettings.Development.json).
        EncryptionHelper.Initialize(Convert.FromBase64String("PuhNcbZ1vD9esnchGmGp+BuZXA5zRYJww241P0qp1DI="));
    }
}

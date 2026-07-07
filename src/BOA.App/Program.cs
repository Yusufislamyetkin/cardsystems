using CoreWCF;
using CoreWCF.Configuration;
using CoreWCF.Description;
using BOA.Common.Contracts.ServiceContracts;
using BOA.Services.Card;
using BOA.Data;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Json;

var builder = WebApplication.CreateBuilder(args);

// 1. Kurumsal Yapılandırılmış Loglama (Serilog) Kurulumu
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(new JsonFormatter(renderMessage: true), "logs/boa_app_log.json", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// 2. Sunucu Çalışma Portlarının ve HTTPS Desteğinin Tanımlanması
// Hem HTTP hem de şifreli HTTPS uç noktalarını dinliyoruz.
builder.WebHost.UseUrls("http://localhost:5000", "https://localhost:5001");

// 3. Denetleyicilerin (REST Controllers) Eklenmesi
builder.Services.AddControllers();

// 4. BOA Mimari Bileşenlerinin Dependency Injection (DI) Konteynerine Eklenmesi
var providerType = builder.Configuration["DatabaseSettings:Provider"] ?? "SqliteMock";
var connStr = builder.Configuration["DatabaseSettings:ConnectionString"] ?? "";

// Seçilen veritabanı tipine göre sağlayıcıyı (Provider) oluşturup DI konteynerine ekliyoruz
BOA.Data.Providers.IBoaDbProvider dbProvider = providerType.ToLowerInvariant() switch
{
    "oracle" => new BOA.Data.Providers.OracleProvider(connStr),
    "postgres" => new BOA.Data.Providers.PostgresProvider(connStr),
    _ => new BOA.Data.Providers.SqliteMockProvider()
};

// PAN/PIN şifreleme anahtarı: kaynak kodda değil, ortam değişkeni veya appsettings'ten (KMS/HSM yerine
// yerel geliştirme sürümü) okunur. EncryptionSettings:Key, 32 byte'lık Base64 kodlanmış bir AES-256 anahtarıdır.
var encryptionKeyBase64 = Environment.GetEnvironmentVariable("BOA_ENCRYPTION_KEY")
    ?? builder.Configuration["EncryptionSettings:Key"]
    ?? throw new InvalidOperationException(
        "Şifreleme anahtarı bulunamadı. BOA_ENCRYPTION_KEY ortam değişkenini veya EncryptionSettings:Key yapılandırmasını ayarlayın.");
BOA.Data.Helpers.EncryptionHelper.Initialize(Convert.FromBase64String(encryptionKeyBase64));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<BOA.Data.Providers.IBoaDbProvider>(dbProvider);
builder.Services.AddSingleton<DbManager>();

// CardService, istek bazlı (Scoped) ICurrentUserContext'e bağımlı olduğu için Scoped kaydedilir.
// Singleton olsaydı ICurrentUserContext yalnızca ilk istekte enjekte edilir ve sonraki tüm
// isteklerde aynı (yanlış) kullanıcı/rol bilgisi paylaşılırdı (captive dependency).
builder.Services.AddScoped<ICurrentUserContext, CurrentUserContext>();

// Dış kart işleme sağlayıcısı (PayCore) entegrasyon sınırı. Kendi ayrı veritabanını (paycore_mock.db)
// kullandığı için Singleton — gerçek bir entegrasyonda bunun yerine bir HTTP istemcisi (IHttpClientFactory
// ile) kaydedilirdi.
builder.Services.AddSingleton<BOA.Services.Card.Paycore.IPaycoreGateway>(new BOA.Services.Card.Paycore.PaycoreMockGateway());

builder.Services.AddScoped<CardService>();
builder.Services.AddScoped<ICardService, CardService>();

// 5. CoreWCF SOAP Servis Altyapısının Yapılandırılması
builder.Services.AddServiceModelServices();

// WSDL Desteğinin Aktif Edilmesi
var serviceMetadataBehavior = new ServiceMetadataBehavior
{
    HttpGetEnabled = true,
    HttpsGetEnabled = true // HTTPS üzerinden WSDL alımına izin ver
};
builder.Services.AddSingleton<IServiceBehavior>(serviceMetadataBehavior);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRouting();
app.MapControllers();

// 6. CoreWCF SOAP Uç Noktalarının Güvenli Binding'ler ile Tanımlanması
app.UseServiceModel(serviceBuilder =>
{
    serviceBuilder.AddService<CardService>();
    
    // A. HTTP Uç Noktası (Hardened BasicHttpBinding)
    serviceBuilder.AddServiceEndpoint<CardService, ICardService>(
        GetHardenedBinding(false), 
        "/CardService.svc"
    );

    // B. HTTPS Uç Noktası (Hardened & Transport Security BasicHttpBinding)
    serviceBuilder.AddServiceEndpoint<CardService, ICardService>(
        GetHardenedBinding(true), 
        "/CardService.svc"
    );
});

// Sunucuyu başlat
try
{
    Log.Information("BOA CoreWCF Enterprise SOAP Server Başlatılıyor...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Sunucu başlatılırken kritik hata oluştu!");
}
finally
{
    Log.CloseAndFlush();
}

// Güvenlik ve performans için sıkılaştırılmış Binding üreten metot
BasicHttpBinding GetHardenedBinding(bool isHttps)
{
    var binding = new BasicHttpBinding();
    
    if (isHttps)
    {
        binding.Security.Mode = CoreWCF.Channels.BasicHttpSecurityMode.Transport;
    }

    // Performans ve Güvenlik Limitleri (DoS Önleme)
    binding.MaxReceivedMessageSize = 1048576; // Maksimum 1MB XML paketi
    binding.ReceiveTimeout = TimeSpan.FromSeconds(15);
    binding.SendTimeout = TimeSpan.FromSeconds(15);
    binding.OpenTimeout = TimeSpan.FromSeconds(15);
    binding.CloseTimeout = TimeSpan.FromSeconds(15);

    binding.ReaderQuotas.MaxDepth = 32;
    binding.ReaderQuotas.MaxStringContentLength = 16384;
    binding.ReaderQuotas.MaxArrayLength = 16384;

    return binding;
}

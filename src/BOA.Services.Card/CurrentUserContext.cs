namespace BOA.Services.Card;

/// <summary>
/// İstek başına (per-request) kimlik/rol bilgisini taşır. Eskiden bu bilgi <see cref="System.Threading.Thread.CurrentPrincipal"/>
/// üzerinden tutuluyordu; bu, ASP.NET Core'un thread-pool tabanlı, senkronizasyon bağlamı olmayan yapısında
/// eşzamanlı isteklerin birbirinin kimlik/rol bilgisini görmesine (thread yeniden kullanıldığında) yol açabiliyordu.
/// Bu sınıf Scoped olarak kaydedilir; her HTTP/WCF isteğinde yeni bir örneği oluşturulur.
/// </summary>
public class CurrentUserContext : ICurrentUserContext
{
    public string? UserId { get; private set; }
    public string? Role { get; private set; }

    public void Set(string userId, string role)
    {
        UserId = userId;
        Role = role;
    }

    public bool IsInRole(string role) => string.Equals(Role, role, StringComparison.Ordinal);
}

public interface ICurrentUserContext
{
    string? UserId { get; }
    string? Role { get; }
    void Set(string userId, string role);
    bool IsInRole(string role);
}

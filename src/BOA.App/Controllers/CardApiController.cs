using Microsoft.AspNetCore.Mvc;
using BOA.Common.Contracts.Requests;
using BOA.Common.Contracts.ServiceContracts;
using BOA.Services.Card;
using BOA.Data;

namespace BOA.App.Controllers;

/// <summary>
/// Web arayüzünün (SPA) JSON formatında istek atabilmesi için geliştirilmiş API ağ geçididir (Gateway).
/// Bu sınıf, gelen REST isteklerini alır ve arka planda çalışan WCF ICardService katmanına yönlendirir.
/// </summary>
[ApiController]
[Route("api/cards")]
public class CardApiController : ControllerBase
{
    private readonly ICardService _cardService;

    /// <summary>
    /// Dependency Injection (DI) ile WCF Servis örneğini alır.
    /// </summary>
    public CardApiController(ICardService cardService)
    {
        _cardService = cardService;
    }

    /// <summary>
    /// Yeni kart tanımlama JSON uç noktası.
    /// </summary>
    [HttpPost("create")]
    public IActionResult CreateCard([FromBody] CreateCardRequest request)
    {
        // İstek yapan istemci IP adresini otomatik eşliyoruz
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        
        // Doğrudan WCF servis metodunu tetikliyoruz
        var response = _cardService.CreateCard(request);
        return Ok(response);
    }

    /// <summary>
    /// Filtrelerle kart sorgulama JSON uç noktası.
    /// </summary>
    [HttpPost("list")]
    public IActionResult GetCardList([FromBody] GetCardListRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.GetCardList(request);
        return Ok(response);
    }

    /// <summary>
    /// Kart limit güncelleme JSON uç noktası.
    /// </summary>
    [HttpPost("update-limit")]
    public IActionResult UpdateLimit([FromBody] UpdateCardLimitRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.UpdateCardLimit(request);
        return Ok(response);
    }

    /// <summary>
    /// Kart blokaj/durum güncelleme JSON uç noktası.
    /// </summary>
    [HttpPost("set-status")]
    public IActionResult SetStatus([FromBody] SetCardStatusRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.SetCardStatus(request);
        return Ok(response);
    }

    /// <summary>
    /// Kart hareketlerini sorgulama JSON uç noktası.
    /// </summary>
    [HttpPost("transactions")]
    public IActionResult GetTransactions([FromBody] GetCardTransactionsRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.GetCardTransactions(request);
        return Ok(response);
    }

    /// <summary>
    /// Kart üzerinde işlem simülasyonu JSON uç noktası.
    /// </summary>
    [HttpPost("create-transaction")]
    public IActionResult CreateTransaction([FromBody] CreateTransactionRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.CreateTransaction(request);
        return Ok(response);
    }

    /// <summary>
    /// Kart şifre doğrulama (PIN Verify) JSON uç noktası.
    /// </summary>
    [HttpPost("verify-pin")]
    public IActionResult VerifyPin([FromBody] VerifyPinRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.VerifyPin(request);
        return Ok(response);
    }

    /// <summary>
    /// Provizyon (authorization/hold) alma JSON uç noktası.
    /// </summary>
    [HttpPost("authorize")]
    public IActionResult AuthorizeTransaction([FromBody] AuthorizeTransactionRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.AuthorizeTransaction(request);
        return Ok(response);
    }

    /// <summary>
    /// Provizyonu kesinleştirme (Capture) JSON uç noktası.
    /// </summary>
    [HttpPost("capture")]
    public IActionResult CaptureAuthorization([FromBody] CaptureAuthorizationRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.CaptureAuthorization(request);
        return Ok(response);
    }

    /// <summary>
    /// Provizyonu iptal etme (Void) JSON uç noktası.
    /// </summary>
    [HttpPost("void")]
    public IActionResult VoidAuthorization([FromBody] VoidAuthorizationRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.VoidAuthorization(request);
        return Ok(response);
    }

    /// <summary>
    /// Gün sonu (End of Day) batch sürecini tetikleyen JSON uç noktası. Gerçek bir bankada bu süreç
    /// gece yarısı zamanlanmış bir iş olarak otomatik çalışır; burada manuel tetikleme sağlanır.
    /// </summary>
    [HttpPost("run-eod-batch")]
    public IActionResult RunEodBatch([FromBody] RunEodBatchRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.RunEodBatch(request);
        return Ok(response);
    }

    /// <summary>
    /// Bir karta ait ekstre (hesap kesimi) geçmişini listeleyen JSON uç noktası.
    /// </summary>
    [HttpPost("statements")]
    public IActionResult GetCardStatements([FromBody] GetCardStatementsRequest request)
    {
        request.ClientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
        var response = _cardService.GetCardStatements(request);
        return Ok(response);
    }

    /// <summary>
    /// Arka planda gerçekleşen WCF SOAP çağrı loglarını çeker.
    /// UI ekranındaki "WCF SOAP Log Tracer" paneli bu uç noktayı dinler.
    /// </summary>
    [HttpGet("/api/tracer/wcf-logs")]
    public IActionResult GetWcfLogs()
    {
        lock (CardService.WcfExecutionLogs)
        {
            // Log listesinin bir kopyasını geri döndürüyoruz
            return Ok(CardService.WcfExecutionLogs.ToList());
        }
    }

    /// <summary>
    /// Arka planda veritabanında çalıştırılan Stored Procedure çağrı loglarını çeker.
    /// UI ekranındaki "SQL Stored Procedure Tracer" paneli bu uç noktayı dinler.
    /// </summary>
    [HttpGet("/api/tracer/sql-logs")]
    public IActionResult GetSqlLogs()
    {
        lock (DbManager.SqlExecutionLogs)
        {
            return Ok(DbManager.SqlExecutionLogs.ToList());
        }
    }

    /// <summary>
    /// İzleme loglarını temizler (Paneli sıfırlamak için).
    /// </summary>
    [HttpPost("/api/tracer/clear")]
    public IActionResult ClearTracer()
    {
        lock (CardService.WcfExecutionLogs)
        {
            CardService.WcfExecutionLogs.Clear();
        }
        lock (DbManager.SqlExecutionLogs)
        {
            DbManager.SqlExecutionLogs.Clear();
        }
        return Ok(new { Message = "Tracer logları temizlendi." });
    }
}

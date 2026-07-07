using System.Runtime.Serialization;
using BOA.Common.Contracts.Base;

namespace BOA.Common.Contracts.Requests;

/// <summary>
/// BDDK/TCMB regulatuar raporlamasi icin gunluk/portfoy ozet raporu istegidir.
/// daily_summary: basvuru/onay/red/basim sayilari (o gun).
/// portfolio_risk: toplam kart sayisi, hacim, gecikme orani.
/// </summary>
[DataContract]
public class GetRegulatoryReportRequest : RequestBase
{
    [DataMember]
    public string ReportType { get; set; } = "daily_summary";

    [DataMember]
    public DateTime? ReportDate { get; set; }
}

[DataContract]
public class GetRegulatoryReportResponse : ResponseBase
{
    [DataMember]
    public string ReportType { get; set; } = string.Empty;

    [DataMember]
    public string ReportDate { get; set; } = string.Empty;

    /// <summary>JSON formatinda rapor detaylari - BDDK tablo yapisina uygun.</summary>
    [DataMember]
    public string ReportData { get; set; } = "{}";
}
using System.Data;
using BOA.Common.Contracts.Dtos;
using BOA.Common.Contracts.Enums;

namespace BOA.Services.Card;

/// <summary>
/// Veritabanından dönen DataRow satırlarını DTO'lara çeviren saf (side-effect'siz) dönüştürme
/// fonksiyonlarını bir araya toplar. Bunlar önceden CardService içinde özel (private) metotlardı;
/// CardService'in "God Class" büyüklüğünü azaltmak amacıyla buraya taşındı — davranışları değişmedi.
/// </summary>
internal static class CardMappers
{
    public static CardDto ToCardDto(DataRow row)
    {
        return new CardDto
        {
            CardId = Convert.ToInt32(row["card_id"]),
            CardNumber = row["card_number"].ToString() ?? string.Empty,
            CardHolderName = row["card_holder_name"].ToString() ?? string.Empty,
            EmbossName = row.Table.Columns.Contains("emboss_name") && row["emboss_name"] != DBNull.Value ? row["emboss_name"].ToString() ?? string.Empty : string.Empty,
            CardType = (CardType)Convert.ToInt32(row["card_type"]),
            CardBrand = row.Table.Columns.Contains("card_brand") ? (CardBrand)Convert.ToInt32(row["card_brand"]) : CardBrand.Troy,
            CardProduct = row.Table.Columns.Contains("card_product") ? (CardProduct)Convert.ToInt32(row["card_product"]) : CardProduct.Classic,
            ExpiryDate = Convert.ToDateTime(row["expiry_date"]),
            Status = (CardStatus)Convert.ToInt32(row["status"]),
            CardLimit = Convert.ToDecimal(row["card_limit"]),
            Balance = Convert.ToDecimal(row["balance"]),
            CreatedDate = Convert.ToDateTime(row["created_date"]),
            CustomerId = row.Table.Columns.Contains("customer_id") ? Convert.ToInt32(row["customer_id"]) : 0,
            BankAccountId = row.Table.Columns.Contains("bank_account_id") ? Convert.ToInt32(row["bank_account_id"]) : 0,
            NationalId = row.Table.Columns.Contains("national_id") ? (row["national_id"].ToString() ?? string.Empty) : string.Empty,
            PaycoreReference = row.Table.Columns.Contains("paycore_reference") && row["paycore_reference"] != DBNull.Value ? row["paycore_reference"].ToString() : null,
            Cvv2Hash = row.Table.Columns.Contains("cvv2_hash") && row["cvv2_hash"] != DBNull.Value ? row["cvv2_hash"].ToString() : null,
            CvvHash = row.Table.Columns.Contains("cvv_hash") && row["cvv_hash"] != DBNull.Value ? row["cvv_hash"].ToString() : null,
            ServiceCode = row.Table.Columns.Contains("service_code") ? (row["service_code"].ToString() ?? "201") : "201",
            Track2Data = row.Table.Columns.Contains("track2_data") && row["track2_data"] != DBNull.Value ? row["track2_data"].ToString() : null
        };
    }

    public static TransactionDto ToTransactionDto(DataRow row)
    {
        return new TransactionDto
        {
            TransactionId = Convert.ToInt32(row["transaction_id"]),
            CardId = Convert.ToInt32(row["card_id"]),
            TransactionType = (TransactionType)Convert.ToInt32(row["transaction_type"]),
            Amount = Convert.ToDecimal(row["amount"]),
            Description = row["description"].ToString() ?? string.Empty,
            TransactionDate = Convert.ToDateTime(row["transaction_date"]),
            ReferenceNumber = row["reference_number"].ToString() ?? string.Empty,
            MerchantId = row.Table.Columns.Contains("merchant_id") && row["merchant_id"] != DBNull.Value ? row["merchant_id"].ToString() : null,
            Mcc = row.Table.Columns.Contains("mcc") && row["mcc"] != DBNull.Value ? row["mcc"].ToString() : null
        };
    }

    public static AuthorizationDto ToAuthorizationDto(DataRow row)
    {
        return new AuthorizationDto
        {
            AuthorizationId = Convert.ToInt32(row["authorization_id"]),
            CardId = Convert.ToInt32(row["card_id"]),
            TransactionType = (TransactionType)Convert.ToInt32(row["transaction_type"]),
            Amount = Convert.ToDecimal(row["amount"]),
            ResponseCode = (AuthResponseCode)Convert.ToInt32(row["response_code"]),
            AuthorizationCode = row["authorization_code"] != DBNull.Value ? row["authorization_code"].ToString() : null,
            Status = (AuthorizationStatus)Convert.ToInt32(row["status"]),
            Description = row["description"].ToString() ?? string.Empty,
            ReferenceNumber = row["reference_number"].ToString() ?? string.Empty,
            MerchantId = row.Table.Columns.Contains("merchant_id") && row["merchant_id"] != DBNull.Value ? row["merchant_id"].ToString() : null,
            Mcc = row.Table.Columns.Contains("mcc") && row["mcc"] != DBNull.Value ? row["mcc"].ToString() : null,
            CreatedDate = Convert.ToDateTime(row["created_date"]),
            PaycoreAuthReference = row.Table.Columns.Contains("paycore_auth_reference") && row["paycore_auth_reference"] != DBNull.Value ? row["paycore_auth_reference"].ToString() : null
        };
    }

    public static LimitChangeRequestDto ToLimitChangeRequestDto(DataRow row)
    {
        return new LimitChangeRequestDto
        {
            LimitRequestId = Convert.ToInt32(row["limit_request_id"]),
            CardId = Convert.ToInt32(row["card_id"]),
            CurrentLimit = Convert.ToDecimal(row["current_limit"]),
            RequestedLimit = Convert.ToDecimal(row["requested_limit"]),
            Status = (LimitChangeRequestStatus)Convert.ToInt32(row["status"]),
            Reason = row["reason"].ToString() ?? string.Empty,
            MakerUserId = row["maker_user_id"].ToString() ?? string.Empty,
            CheckerUserId = row["checker_user_id"] != DBNull.Value ? row["checker_user_id"].ToString() : null,
            DecisionNote = row["decision_note"] != DBNull.Value ? row["decision_note"].ToString() : null,
            CreatedDate = Convert.ToDateTime(row["created_date"]),
            DecidedDate = row["decided_date"] != DBNull.Value ? Convert.ToDateTime(row["decided_date"]) : null
        };
    }

    public static CardApplicationDto ToCardApplicationDto(DataRow row)
    {
        return new CardApplicationDto
        {
            ApplicationId = Convert.ToInt32(row["application_id"]),
            NationalId = row["national_id"].ToString() ?? string.Empty,
            ApplicantName = row["applicant_name"].ToString() ?? string.Empty,
            Phone = row["phone"] != DBNull.Value ? row["phone"].ToString() : null,
            DeclaredMonthlyIncome = Convert.ToDecimal(row["declared_monthly_income"]),
            RequestedLimit = Convert.ToDecimal(row["requested_limit"]),
            CreditScore = Convert.ToInt32(row["credit_score"]),
            BddkLimitCap = Convert.ToDecimal(row["bddk_limit_cap"]),
            ApprovedLimit = row["approved_limit"] != DBNull.Value ? Convert.ToDecimal(row["approved_limit"]) : null,
            Status = (CardApplicationStatus)Convert.ToInt32(row["status"]),
            DecisionReason = row["decision_reason"] != DBNull.Value ? row["decision_reason"].ToString() : null,
            MakerUserId = row["maker_user_id"].ToString() ?? string.Empty,
            CheckerUserId = row["checker_user_id"] != DBNull.Value ? row["checker_user_id"].ToString() : null,
            CardId = row["card_id"] != DBNull.Value ? Convert.ToInt32(row["card_id"]) : null,
            CreatedDate = Convert.ToDateTime(row["created_date"]),
            DecidedDate = row["decided_date"] != DBNull.Value ? Convert.ToDateTime(row["decided_date"]) : null,
            IssuedDate = row["issued_date"] != DBNull.Value ? Convert.ToDateTime(row["issued_date"]) : null
        };
    }

    public static StatementDto ToStatementDto(DataRow row)
    {
        return new StatementDto
        {
            StatementId = Convert.ToInt32(row["statement_id"]),
            CardId = Convert.ToInt32(row["card_id"]),
            StatementDate = Convert.ToDateTime(row["statement_date"]),
            DueDate = Convert.ToDateTime(row["due_date"]),
            TotalDebt = Convert.ToDecimal(row["total_debt"]),
            MinimumPayment = Convert.ToDecimal(row["minimum_payment"]),
            IsPaid = Convert.ToBoolean(row["is_paid"]),
            InterestApplied = Convert.ToBoolean(row["interest_applied"]),
            CreatedDate = Convert.ToDateTime(row["created_date"])
        };
    }
}

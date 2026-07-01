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
            CardType = (CardType)Convert.ToInt32(row["card_type"]),
            ExpiryDate = Convert.ToDateTime(row["expiry_date"]),
            Status = (CardStatus)Convert.ToInt32(row["status"]),
            CardLimit = Convert.ToDecimal(row["card_limit"]),
            Balance = Convert.ToDecimal(row["balance"]),
            CreatedDate = Convert.ToDateTime(row["created_date"]),
            CustomerId = row.Table.Columns.Contains("customer_id") ? Convert.ToInt32(row["customer_id"]) : 0,
            BankAccountId = row.Table.Columns.Contains("bank_account_id") ? Convert.ToInt32(row["bank_account_id"]) : 0,
            NationalId = row.Table.Columns.Contains("national_id") ? (row["national_id"].ToString() ?? string.Empty) : string.Empty
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
            CreatedDate = Convert.ToDateTime(row["created_date"])
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

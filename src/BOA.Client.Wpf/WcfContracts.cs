using System.ServiceModel;
using BOA.Common.Contracts.Requests;
using BOA.Common.Contracts.Base;

namespace BOA.Client.Wpf;

/// <summary>
/// İstemci (WPF) tarafında tanımlanan WCF servis sözleşmesidir.
/// Sunucudaki CoreWCF uç noktaları ile SOAP haberleşmesi yapabilmek için 
/// standard WCF 'System.ServiceModel' özniteliklerini kullanır.
/// </summary>
[ServiceContract(Namespace = "http://emlakkatilim.com.tr/boa/card", Name = "ICardService")]
public interface ICardServiceClient
{
    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CreateCardResponse CreateCard(CreateCardRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetCardListResponse GetCardList(GetCardListRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    UpdateCardLimitResponse UpdateCardLimit(UpdateCardLimitRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    SetCardStatusResponse SetCardStatus(SetCardStatusRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetCardTransactionsResponse GetCardTransactions(GetCardTransactionsRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CreateTransactionResponse CreateTransaction(CreateTransactionRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    VerifyPinResponse VerifyPin(VerifyPinRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    AuthorizeTransactionResponse AuthorizeTransaction(AuthorizeTransactionRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    CaptureAuthorizationResponse CaptureAuthorization(CaptureAuthorizationRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    VoidAuthorizationResponse VoidAuthorization(VoidAuthorizationRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    RunEodBatchResponse RunEodBatch(RunEodBatchRequest request);

    [OperationContract]
    [FaultContract(typeof(BankingFault))]
    GetCardStatementsResponse GetCardStatements(GetCardStatementsRequest request);
}

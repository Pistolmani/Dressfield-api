using System.Net;
using System.Text;
using Dressfield.Infrastructure.Services;
using Dressfield.Tests.TestInfrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dressfield.Tests.Services;

public sealed class BogIPayServiceTests
{
    private static BogIPayService Build(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var config = TestConfig.Build(
            ("BogIPay:ApiBaseUrl", "https://api.test.local"),
            ("BogIPay:FrontendBaseUrl", "https://test.local"),
            ("BogIPay:ReceiptUrl", "https://bog.test/receipt"));

        return new BogIPayService(
            new HttpClient(new StubHttpMessageHandler(responder)),
            config,
            new FakeBogTokenProvider(),
            NullLogger<BogIPayService>.Instance);
    }

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task ApprovedReceipt_ParsesAmountCurrencyAndTransaction()
    {
        var svc = Build(_ => Json("""
            {
              "order_status": { "key": "completed" },
              "payment_detail": { "transaction_id": "txn-42" },
              "purchase_units": { "transfer_amount": 65.00, "currency": "GEL" }
            }
            """));

        var result = await svc.VerifyCallbackAsync("bog-42");

        Assert.True(result.IsApproved);
        Assert.Equal("txn-42", result.TransactionId);
        Assert.Equal(65.00m, result.VerifiedAmount);
        Assert.Equal("GEL", result.Currency);
        Assert.False(result.IsTransientFailure);
    }

    [Fact]
    public async Task DeclinedReceipt_MapsToNotApproved_NotTransient()
    {
        var svc = Build(_ => Json("""{ "order_status": { "key": "rejected" } }"""));

        var result = await svc.VerifyCallbackAsync("bog-42");

        Assert.False(result.IsApproved);
        Assert.Equal("rejected", result.Status);
        Assert.False(result.IsTransientFailure);
    }

    [Fact]
    public async Task ServerError_IsTransientFailure()
    {
        var svc = Build(_ => Json("oops", HttpStatusCode.InternalServerError));

        var result = await svc.VerifyCallbackAsync("bog-42");

        Assert.False(result.IsApproved);
        Assert.True(result.IsTransientFailure);
    }

    [Fact]
    public async Task MalformedJson_IsTransientFailure()
    {
        var svc = Build(_ => Json("{not json"));

        var result = await svc.VerifyCallbackAsync("bog-42");

        Assert.False(result.IsApproved);
        Assert.True(result.IsTransientFailure);
    }

    [Fact]
    public async Task NetworkException_IsTransientFailure()
    {
        var svc = Build(_ => throw new HttpRequestException("connection refused"));

        var result = await svc.VerifyCallbackAsync("bog-42");

        Assert.True(result.IsTransientFailure);
    }

    [Fact]
    public async Task Lookup_WithoutConfiguredEndpoint_ReturnsNull()
    {
        var svc = Build(_ => Json("{}"));

        var result = await svc.LookupByExternalOrderIdAsync("key-1");

        Assert.Null(result);
    }
}

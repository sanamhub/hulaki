using System;
using System.Net;
using Hulaki.Channels;
using Xunit;

namespace Hulaki.Tests;

public sealed class HttpHelpersTests
{
    [Theory]
    [InlineData(400, HulakiErrorCode.InvalidInput, RetryDisposition.Never)]
    [InlineData(413, HulakiErrorCode.InvalidInput, RetryDisposition.Never)]
    [InlineData(415, HulakiErrorCode.InvalidInput, RetryDisposition.Never)]
    [InlineData(422, HulakiErrorCode.InvalidInput, RetryDisposition.Never)]
    [InlineData(401, HulakiErrorCode.Unauthorized, RetryDisposition.AfterReconnect)]
    [InlineData(402, HulakiErrorCode.BillingRequired, RetryDisposition.Never)]
    [InlineData(403, HulakiErrorCode.PermissionDenied, RetryDisposition.Never)]
    [InlineData(404, HulakiErrorCode.RecipientNotFound, RetryDisposition.Never)]
    [InlineData(410, HulakiErrorCode.RecipientNotFound, RetryDisposition.Never)]
    [InlineData(408, HulakiErrorCode.Timeout, RetryDisposition.AfterDelay)]
    [InlineData(429, HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay)]
    [InlineData(503, HulakiErrorCode.UpstreamFailure, RetryDisposition.AfterDelay)]
    [InlineData(500, HulakiErrorCode.UpstreamFailure, RetryDisposition.ReconcileFirst)]
    [InlineData(502, HulakiErrorCode.UpstreamFailure, RetryDisposition.ReconcileFirst)]
    [InlineData(418, HulakiErrorCode.UpstreamFailure, RetryDisposition.Never)]
    public void Error_for_maps_each_status(int status, HulakiErrorCode code, RetryDisposition retry)
    {
        var retryAfter = TimeSpan.FromSeconds(7);

        var error = HttpHelpers.ErrorFor((HttpStatusCode)status, retryAfter, "platform said no");

        Assert.Equal(code, error.Code);
        Assert.Equal(retry, error.Retry);
        Assert.Equal(status, error.HttpStatus);
        Assert.Equal(retryAfter, error.RetryAfter);
        Assert.Equal("platform said no", error.Message);
    }

    [Theory]
    [InlineData(RetryDisposition.ReconcileFirst, DeliveryStatus.Unknown)]
    [InlineData(RetryDisposition.AfterDelay, DeliveryStatus.Failed)]
    [InlineData(RetryDisposition.Never, DeliveryStatus.Failed)]
    public void Outcome_for_is_unknown_only_when_the_platform_may_have_acted(RetryDisposition retry, DeliveryStatus status)
    {
        var outcome = HttpHelpers.OutcomeFor(new HulakiError(HulakiErrorCode.UpstreamFailure, retry, "x"));

        Assert.Equal(status, outcome.Status);
    }
}

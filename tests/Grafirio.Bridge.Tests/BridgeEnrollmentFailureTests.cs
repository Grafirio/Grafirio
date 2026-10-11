using System.Net;

namespace Grafirio.Bridge.Tests;

public sealed class BridgeEnrollmentFailureTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "BRIDGE_IDENTITY_UNAVAILABLE", false)]
    [InlineData(HttpStatusCode.Unauthorized, "SESSION_EXPIRED", false)]
    [InlineData(HttpStatusCode.Forbidden, "ENROLLMENT_FORBIDDEN", false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ENROLLMENT_UNAVAILABLE", true)]
    public async Task EnrollmentRetainsSafeActionableFailure(HttpStatusCode status, string code, bool retryable)
    {
        using var context = new BridgeSecurityTestContext();
        context.StatusCode = status;
        context.ResponseBody = BridgeSecurityTestContext.Secret + BridgeSecurityTestContext.InstallerToken;
        var enrollment = context.CreateEnrollment();
        Assert.False(await enrollment.EnrollWithTokenAsync(BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        Assert.Equal(code, enrollment.LastFailure!.Code);
        Assert.Equal(retryable, enrollment.LastFailure.Retryable);
        Assert.Contains(((int)status).ToString(), enrollment.LastFailure.Message);
        Assert.DoesNotContain(BridgeSecurityTestContext.Secret, enrollment.LastFailure.Message);
        context.AssertNoSecretsExposed();
    }

    [Fact]
    public async Task SuccessfulRetryClearsPreviousFailure()
    {
        using var context = new BridgeSecurityTestContext();
        var enrollment = context.CreateEnrollment();
        context.StatusCode = HttpStatusCode.BadGateway;
        Assert.False(await enrollment.EnrollWithTokenAsync(BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        context.StatusCode = HttpStatusCode.OK;
        context.SetEnrollmentResponse();
        Assert.True(await enrollment.EnrollWithTokenAsync(BridgeSecurityTestContext.InstallerToken, CancellationToken.None));
        Assert.Null(enrollment.LastFailure);
    }
}

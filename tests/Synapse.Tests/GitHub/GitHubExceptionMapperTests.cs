using System.Net;
using Shouldly;
using Synapse.Core.Ports;
using Synapse.Sync.GitHub;

namespace Synapse.Tests.GitHub;

public class GitHubExceptionMapperTests
{
    [Fact]
    public void Map_When401Unauthorized_ShouldReturnCloudAuthExpiredException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        var mapped = GitHubExceptionMapper.Map(response, "Bad credentials");

        mapped.ShouldBeOfType<CloudAuthExpiredException>();
        mapped.Message.ShouldContain("401");
    }

    [Fact]
    public void Map_When404NotFound_ShouldReturnCloudNotFoundException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var mapped = GitHubExceptionMapper.Map(response, "Not Found");

        mapped.ShouldBeOfType<CloudNotFoundException>();
    }

    [Fact]
    public void Map_When403WithRateLimitZero_ShouldReturnCloudQuotaExceededException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "0");

        var mapped = GitHubExceptionMapper.Map(response, "API rate limit exceeded");

        mapped.ShouldBeOfType<CloudQuotaExceededException>();
    }

    [Fact]
    public void Map_When503ServiceUnavailable_ShouldReturnCloudTransientException()
    {
        var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        var mapped = GitHubExceptionMapper.Map(response, "Server Error");

        mapped.ShouldBeOfType<CloudTransientException>();
    }

    [Fact]
    public void Map_WhenHttpRequestException_ShouldReturnCloudTransientException()
    {
        var httpEx = new HttpRequestException("Network failure");
        var mapped = GitHubExceptionMapper.Map(httpEx);

        mapped.ShouldBeOfType<CloudTransientException>();
    }
}

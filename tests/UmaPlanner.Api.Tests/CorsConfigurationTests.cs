using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using UmaPlanner.Api;
using Xunit;

namespace UmaPlanner.Api.Tests;

public sealed class CorsConfigurationTests
{
    [Theory]
    [InlineData("http://localhost:5173")]
    public void Development_allows_loopback_origins(string origin)
    {
        var policy = CreatePolicy(
            new Dictionary<string, string?>(),
            Environments.Development);

        var result = Evaluate(policy, origin);

        Assert.True(result.IsOriginAllowed);
        Assert.Equal(origin, result.AllowedOrigin);
        Assert.True(result.SupportsCredentials);
    }

    [Fact]
    public void Allows_matching_vercel_preview_origin()
    {
        var policy = CreatePolicy(
            new Dictionary<string, string?>
            {
                ["Cors:VercelProjectPrefix"] = "umaplanner-"
            },
            Environments.Production);

        var result = Evaluate(policy, "https://umaplanner-git-feature-necalya.vercel.app");

        Assert.True(result.IsOriginAllowed);
        Assert.Equal("https://umaplanner-git-feature-necalya.vercel.app", result.AllowedOrigin);
        Assert.True(result.SupportsCredentials);
    }

    [Fact]
    public void Allows_configured_exact_origin()
    {
        var policy = CreatePolicy(
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins"] = "https://umaplanner.app"
            },
            Environments.Production);

        var result = Evaluate(policy, "https://umaplanner.app");

        Assert.True(result.IsOriginAllowed);
        Assert.Equal("https://umaplanner.app", result.AllowedOrigin);
    }

    [Theory]
    [InlineData("https://other-project-git-feature.vercel.app")]
    [InlineData("http://umaplanner-git-feature-necalya.vercel.app")]
    [InlineData("https://umaplanner.example.com")]
    public void Rejects_unapproved_origins(string origin)
    {
        var policy = CreatePolicy(
            new Dictionary<string, string?>
            {
                ["Cors:VercelProjectPrefix"] = "umaplanner-"
            },
            Environments.Production);

        var result = Evaluate(policy, origin);

        Assert.False(result.IsOriginAllowed);
    }

    [Fact]
    public void Preflight_allows_configured_method_and_headers_with_credentials()
    {
        var policy = CreatePolicy(
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins"] = "https://umaplanner.app"
            },
            Environments.Production);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Options;
        context.Request.Headers.Origin = "https://umaplanner.app";
        context.Request.Headers.AccessControlRequestMethod = "GET";
        context.Request.Headers.AccessControlRequestHeaders = "Content-Type";

        var result = new CorsService(
            Microsoft.Extensions.Options.Options.Create(new CorsOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .EvaluatePolicy(context, policy);

        Assert.True(result.IsOriginAllowed);
        Assert.Equal("https://umaplanner.app", result.AllowedOrigin);
        Assert.Equal("GET", result.AllowedMethods.Single());
        Assert.Contains("Content-Type", result.AllowedHeaders);
        Assert.True(result.SupportsCredentials);
    }

    private static CorsPolicy CreatePolicy(
        IDictionary<string, string?> settings,
        string environmentName)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var environment = new TestHostEnvironment(environmentName);
        var services = new ServiceCollection()
            .AddLogging()
            .AddConfiguredCors(configuration, environment)
            .BuildServiceProvider();

        return services
            .GetRequiredService<ICorsPolicyProvider>()
            .GetPolicyAsync(new DefaultHttpContext(), "ConfiguredCors")
            .GetAwaiter()
            .GetResult()!;
    }

    private static CorsResult Evaluate(CorsPolicy policy, string origin)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Origin = origin;
        return new CorsService(
            Microsoft.Extensions.Options.Options.Create(new CorsOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance)
            .EvaluatePolicy(context, policy);
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "UmaPlanner.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}

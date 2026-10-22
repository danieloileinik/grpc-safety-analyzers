using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class RetryConfigurationTests
{
    [Fact]
    public async Task StableLocalMethodConfigIsResolved()
    {
        // Arrange
        var setup = "MethodConfig method = {|#0:" + RetryTestSource.Method() + "|};";
        var test = CreateTest(RetryTestSource.Wrap("method", setup: setup));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("HedgingDelay = TimeSpan.Zero,", "")]
    [InlineData("HedgingDelay = TimeSpan.FromSeconds(1),", "NonFatalStatusCodes = { StatusCode.Unavailable }")]
    public async Task SupportedHedgingPolicyIsResolved(string delay, string codes)
    {
        // Arrange
        var method = RetryTestSource.Method("new HedgingPolicy { MaxAttempts = 2, " + delay + codes + " }");
        var test = CreateTest(RetryTestSource.Wrap("{|#0:" + method + "|}"));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("method.RetryPolicy.MaxAttempts = 1;")]
    [InlineData("Change(method);")]
    [InlineData("MethodConfig alias = method;")]
    [InlineData("method = new MethodConfig();")]
    [InlineData("method.Names.Clear();")]
    [InlineData("Action change = () => method.Names.Clear();")]
    public async Task MutableOrEscapedMethodConfigurationIsUnknown(string mutation)
    {
        // Arrange
        var setup = "MethodConfig method = " + RetryTestSource.Method() + "; " + mutation;
        var test = CreateTest(RetryTestSource.Wrap("method", setup: setup));
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task StableLocalPolicyIsResolved()
    {
        // Arrange
        var setup = "RetryPolicy policy = " + RetryTestSource.Retry + ";";
        var test = CreateTest(RetryTestSource.Wrap("{|#0:" + RetryTestSource.Method("policy") + "|}", setup: setup));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task SharedPolicyWithAnotherMutableOwnerIsUnknown()
    {
        // Arrange
        var setup = "RetryPolicy policy = " + RetryTestSource.Retry + "; "
                    + "MethodConfig other = new MethodConfig { RetryPolicy = policy }; other.RetryPolicy.MaxAttempts = 1;";
        var test = CreateTest(RetryTestSource.Wrap(RetryTestSource.Method("policy"), setup: setup));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("options.MaxRetryAttempts = 1;", false)]
    [InlineData("options.MaxRetryAttempts = 2;", true)]
    [InlineData("options.ServiceConfig = null;", false)]
    [InlineData("Change(options);", false)]
    public async Task LaterFactoryCallbacksDetermineFinalConfiguration(string callback, bool expected)
    {
        // Arrange
        var method = RetryTestSource.Method();
        if (expected) method = "{|#0:" + method + "|}";
        var source = RetryTestSource.Wrap(method, "options.MaxRetryAttempts = 1;", factory: true);
        source = source.Replace(
            "options.MaxRetryAttempts = 1; });",
            "options.MaxRetryAttempts = 1; }).ConfigureChannel(options => { " + callback + " });");
        var test = CreateTest(source, true);
        if (expected)
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task AmbiguousFactoryRegistrationDoesNotWarn()
    {
        // Arrange
        var source = RetryTestSource.Wrap(RetryTestSource.Method(), factory: true);
        source = source.Replace(
            "services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel",
            "services.AddGrpcClient<Greeter.GreeterClient>(); services.AddGrpcClient<Greeter.GreeterClient>().ConfigureChannel");
        var test = CreateTest(source, true);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task BothRetryAndHedgingOnSameMethodAreUnknown()
    {
        // Arrange
        var method = RetryTestSource
            .Method()
            .Replace("RetryPolicy =", "HedgingPolicy = " + RetryTestSource.Hedging + ", RetryPolicy =");
        var test = CreateTest(RetryTestSource.Wrap(method));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("error")]
    [InlineData("none")]
    public async Task SeverityCanBeConfigured(string severity)
    {
        // Arrange
        var method = RetryTestSource.Method();
        if (severity == "error") method = "{|#0:" + method + "|}";
        var test = CreateTest(RetryTestSource.Wrap(method));
        test.Configure("dotnet_diagnostic.GRPC003.severity = " + severity);
        if (severity == "error")
        {
            test.SolutionTransforms.Add((solution, projectId) =>
            {
                var options = solution.GetProject(projectId)!.CompilationOptions!;
                return solution.WithProjectCompilationOptions(
                    projectId,
                    options.WithSpecificDiagnosticOptions(
                        options.SpecificDiagnosticOptions.SetItem(
                            DiagnosticIds.UndeclaredIdempotency,
                            ReportDiagnostic.Error)));
            });
            test.ExpectedDiagnostics.Add(
                new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency)
                    .WithSeverity(DiagnosticSeverity.Error)
                    .WithLocation(0));
        }

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    private static GrpcAnalyzerTest CreateTest(string source, bool factory = false)
    {
        var test = new GrpcAnalyzerTest { TestCode = source };
        test.TestState.AdditionalFiles.Add(("service.proto", RetryTestSource.Proto));
        if (factory) test.AddClientFactoryReferences();
        return test;
    }
}

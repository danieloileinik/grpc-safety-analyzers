using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class UndeclaredIdempotencyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task ConfirmedMultipleAttemptsRequireDeclaredIdempotency(bool hedging, bool factory)
    {
        // Arrange
        var method = RetryTestSource.Method(hedging ? RetryTestSource.Hedging : RetryTestSource.Retry);
        var test = CreateTest("{|#0:" + method + "|}", factory: factory);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("ReadHello")]
    [InlineData("SaveHello")]
    [InlineData("NotInProto")]
    public async Task ProtectedOrUnmatchedMethodDoesNotWarn(string method)
    {
        // Arrange
        var test = CreateTest(
            RetryTestSource.Method(
                name:
                "new MethodName { Service = \"safety.testing.Greeter\", Method = \"" + method + "\" }"));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("MethodName.Default")]
    [InlineData("new MethodName()")]
    [InlineData("new MethodName { Service = \"missing.Greeter\", Method = \"SayHello\" }")]
    [InlineData("new MethodName { Service = \"Greeter\", Method = \"SayHello\" }")]
    [InlineData("new MethodName { Method = \"SayHello\" }")]
    [InlineData("new MethodName { Service = \"\", Method = \"SayHello\" }")]
    public async Task UnknownSelectorDoesNotWarn(string name)
    {
        // Arrange
        var test = CreateTest(RetryTestSource.Method(name: name));
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(", MaxRetryAttempts = 1", false)]
    [InlineData("options.MaxRetryAttempts = 1;", true)]
    [InlineData(", MaxRetryAttempts = Attempts()", false)]
    [InlineData("options.MaxRetryAttempts = Attempts();", true)]
    [InlineData(", MaxRetryAttempts = 0", false)]
    [InlineData(", MaxRetryAttempts = -1", false)]
    public async Task DisabledOrUnknownChannelLimitDoesNotWarn(string settings, bool factory)
    {
        // Arrange
        var test = CreateTest(RetryTestSource.Method(), settings, factory: factory);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(", MaxRetryAttempts = null", false)]
    [InlineData("options.MaxRetryAttempts = null;", true)]
    [InlineData(", MaxRetryAttempts = 2", false)]
    public async Task UnlimitedOrMultipleAttemptChannelStillWarns(string settings, bool factory)
    {
        // Arrange
        var test = CreateTest("{|#0:" + RetryTestSource.Method() + "|}", settings, factory: factory);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("MaxAttempts = 1", "")]
    [InlineData("MaxAttempts = 3", "")]
    [InlineData(
        "MaxAttempts = Attempts()",
        ", InitialBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(5), BackoffMultiplier = 2, RetryableStatusCodes = { StatusCode.Unavailable }")]
    [InlineData(
        "MaxAttempts = 3",
        ", InitialBackoff = TimeSpan.Zero, MaxBackoff = TimeSpan.FromSeconds(5), BackoffMultiplier = 2, RetryableStatusCodes = { StatusCode.Unavailable }")]
    [InlineData(
        "MaxAttempts = 3",
        ", InitialBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(5), BackoffMultiplier = 0, RetryableStatusCodes = { StatusCode.Unavailable }")]
    [InlineData(
        "MaxAttempts = 3",
        ", InitialBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(5), BackoffMultiplier = double.PositiveInfinity, RetryableStatusCodes = { StatusCode.Unavailable }")]
    [InlineData(
        "MaxAttempts = 3",
        ", InitialBackoff = TimeSpan.FromSeconds(1), MaxBackoff = TimeSpan.FromSeconds(5), BackoffMultiplier = 2")]
    public async Task InvalidOrUnknownRetryPolicyDoesNotWarn(string attempts, string other)
    {
        // Arrange
        var test = CreateTest(RetryTestSource.Method("new RetryPolicy { " + attempts + other + " }"));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("new HedgingPolicy()")]
    [InlineData("new HedgingPolicy { MaxAttempts = 1 }")]
    [InlineData("new HedgingPolicy { MaxAttempts = Attempts() }")]
    [InlineData("new HedgingPolicy { MaxAttempts = 3, HedgingDelay = TimeSpan.FromSeconds(-1) }")]
    [InlineData("new HedgingPolicy { MaxAttempts = 3, NonFatalStatusCodes = { (StatusCode)100 } }")]
    [InlineData("null")]
    public async Task InvalidOrUnknownHedgingPolicyDoesNotWarn(string policy)
    {
        // Arrange
        var test = CreateTest(RetryTestSource.Method(policy));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task ServiceWidePolicyChecksAllProtoMethods()
    {
        // Arrange
        var method = RetryTestSource.Method(name: "new MethodName { Service = \"safety.testing.Greeter\" }");
        var test = CreateTest("{|#0:" + method + "|}");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task MoreSpecificNoRetryConfigurationOverridesServicePolicy()
    {
        // Arrange
        var broad = RetryTestSource.Method(name: "new MethodName { Service = \"safety.testing.Greeter\" }");
        var specific =
            "new MethodConfig { Names = { new MethodName { Service = \"safety.testing.Greeter\", Method = \"SayHello\" } } }";
        var test = CreateTest(broad + ", " + specific);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task DuplicateSelectorsRemainUnknown()
    {
        // Arrange
        var test = CreateTest(RetryTestSource.Method() + ", " + RetryTestSource.Method());

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("syntax = \"proto3\"; service Greeter { rpc Broken (")]
    [InlineData("syntax = \"proto3\"; package safety.testing; service Other { }")]
    [InlineData(
        "syntax = \"proto3\"; package safety.testing; service Greeter { rpc SayHello (R) returns (R) { option (custom) = true; } }")]
    public async Task MissingOrUnknownProtoDoesNotWarn(string? proto)
    {
        // Arrange
        var test = CreateTest(RetryTestSource.Method());
        test.TestState.AdditionalFiles.Clear();
        if (proto is not null) test.TestState.AdditionalFiles.Add(("service.proto", proto));
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    private static GrpcAnalyzerTest CreateTest(string methods,
        string settings = "",
        string setup = "",
        bool factory = false)
    {
        var test = new GrpcAnalyzerTest { TestCode = RetryTestSource.Wrap(methods, settings, setup, factory) };
        test.TestState.AdditionalFiles.Add(("service.proto", RetryTestSource.Proto));
        if (factory) test.AddClientFactoryReferences();
        return test;
    }
}

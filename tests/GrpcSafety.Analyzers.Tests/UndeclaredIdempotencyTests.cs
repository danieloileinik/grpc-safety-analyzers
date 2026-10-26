using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis;
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
    public async Task ConfirmedMultipleAttemptsWarnWhenIdempotencyIsNotGuaranteed(bool hedging, bool factory)
    {
        // Arrange
        var method = RetryTestSource.Method(hedging ? RetryTestSource.Hedging : RetryTestSource.Retry);
        var test = CreateTest("{|#0:" + method + "|}", factory: factory);
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticIds.UndeclaredIdempotency, DiagnosticSeverity.Warning)
                .WithMessage("Retry may be unsafe because RPC idempotency is not guaranteed.")
                .WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("ReadHello")]
    [InlineData("SaveHello")]
    public async Task DeclaredIdempotencyDoesNotWarn(string method)
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
    public async Task MissingOrUnknownProtoWarnsWhenIdempotencyIsNotGuaranteed(string? proto)
    {
        // Arrange
        var test = CreateTest("{|#0:" + RetryTestSource.Method() + "|}");
        test.TestState.AdditionalFiles.Clear();
        if (proto is not null) test.TestState.AdditionalFiles.Add(("service.proto", proto));
        test.Configure("grpc_safety.strict = true");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("Get", false)]
    [InlineData("GetOrder", false)]
    [InlineData("List", false)]
    [InlineData("ListOrders", false)]
    [InlineData("Find", false)]
    [InlineData("FindOrder", false)]
    [InlineData("Create", true)]
    [InlineData("CreateOrder", true)]
    [InlineData("Delete", true)]
    [InlineData("DeleteOrder", true)]
    [InlineData("Increment", true)]
    [InlineData("IncrementCounter", true)]
    [InlineData("Charge", true)]
    [InlineData("ChargeCard", true)]
    [InlineData("Append", true)]
    [InlineData("AppendEvent", true)]
    [InlineData("UpdateOrder", true)]
    [InlineData("ReadOrder", true)]
    [InlineData("getOrder", true)]
    public async Task MethodNameDeterminesPotentialIdempotencyWithoutProto(string rpc, bool warning)
    {
        // Arrange
        string method = RetryTestSource.Method(
            name: "new MethodName { Service = \"safety.testing.Greeter\", Method = \"" + rpc + "\" }");
        GrpcAnalyzerTest test = CreateTest(warning ? "{|#0:" + method + "|}" : method);
        test.TestState.AdditionalFiles.Clear();
        if (warning)
            test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("GetOrder", "", false)]
    [InlineData("ListOrders", "", false)]
    [InlineData("FindOrder", "", false)]
    [InlineData("CreateOrder", "", true)]
    [InlineData("DeleteOrder", "", true)]
    [InlineData("IncrementCounter", "", true)]
    [InlineData("ChargeCard", "", true)]
    [InlineData("AppendEvent", "", true)]
    [InlineData("ReadOrder", "", true)]
    [InlineData("GetOrder", "{ option idempotency_level = IDEMPOTENCY_UNKNOWN; }", true)]
    [InlineData("ListOrders", "{ option idempotency_level = IDEMPOTENCY_UNKNOWN; }", true)]
    [InlineData("FindOrder", "{ option idempotency_level = IDEMPOTENCY_UNKNOWN; }", true)]
    [InlineData("CreateOrder", "{ option idempotency_level = IDEMPOTENT; }", false)]
    [InlineData("DeleteOrder", "{ option idempotency_level = NO_SIDE_EFFECTS; }", false)]
    [InlineData("IncrementCounter", "{ option idempotency_level = IDEMPOTENT; }", false)]
    [InlineData("ChargeCard", "{ option idempotency_level = IDEMPOTENT; }", false)]
    [InlineData("AppendEvent", "{ option idempotency_level = IDEMPOTENT; }", false)]
    [InlineData("ReadOrder", "{ option idempotency_level = IDEMPOTENT; }", false)]
    [InlineData("ReadOrder", "{ option idempotency_level = NO_SIDE_EFFECTS; }", false)]
    [InlineData("GetOrder", "{ option (custom) = true; }", false)]
    [InlineData("CreateOrder", "{ option idempotency_level = IDEMPOTENT; option deprecated = true; }", false)]
    [InlineData("CreateOrder", "{ option deprecated = true; option idempotency_level = IDEMPOTENT; }", false)]
    public async Task ProtoDeclarationOverridesMethodNameForServicePolicies(string rpc, string body, bool warning)
    {
        // Arrange
        string method = RetryTestSource.Method(name: "new MethodName { Service = \"safety.testing.Greeter\" }");
        GrpcAnalyzerTest test = CreateTest(warning ? "{|#0:" + method + "|}" : method);
        test.TestState.AdditionalFiles.Clear();
        test.TestState.AdditionalFiles.Add(("service.proto", CreateProto(rpc, body)));
        if (warning)
            test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(false, false, "CreateOrder", "IDEMPOTENT", false)]
    [InlineData(true, false, "DeleteOrder", "NO_SIDE_EFFECTS", false)]
    [InlineData(false, true, "ChargeCard", "IDEMPOTENT", false)]
    [InlineData(true, true, "AppendEvent", "IDEMPOTENT", false)]
    [InlineData(false, false, "GetOrder", "IDEMPOTENCY_UNKNOWN", true)]
    [InlineData(true, false, "ListOrders", "IDEMPOTENCY_UNKNOWN", true)]
    [InlineData(false, true, "FindOrder", "IDEMPOTENCY_UNKNOWN", true)]
    [InlineData(true, true, "GetOrder", "IDEMPOTENCY_UNKNOWN", true)]
    public async Task ExplicitDeclarationOverridesNameForRetryAndHedging(
        bool hedging, bool factory, string rpc, string declaration, bool warning)
    {
        // Arrange
        string method = RetryTestSource.Method(
            hedging ? RetryTestSource.Hedging : RetryTestSource.Retry,
            "new MethodName { Service = \"safety.testing.Greeter\", Method = \"" + rpc + "\" }");
        GrpcAnalyzerTest test = CreateTest(warning ? "{|#0:" + method + "|}" : method, factory: factory);
        test.TestState.AdditionalFiles.Clear();
        test.TestState.AdditionalFiles.Add((
            "service.proto", CreateProto(rpc, "{ option idempotency_level = " + declaration + "; }")));
        if (warning)
            test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("new MethodName { Service = \"missing.Greeter\", Method = \"SayHello\" }")]
    [InlineData("new MethodName { Service = \"Greeter\", Method = \"SayHello\" }")]
    [InlineData("new MethodName { Service = \"safety.testing.Greeter\", Method = \"NotInProto\" }")]
    [InlineData("new MethodName { Service = \"missing.Greeter\" }")]
    public async Task UnresolvedContractWarnsWhenIdempotencyIsNotGuaranteed(string name)
    {
        // Arrange
        GrpcAnalyzerTest test = CreateTest("{|#0:" + RetryTestSource.Method(name: name) + "|}");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    private static string CreateProto(string rpc, string body)
    {
        return "syntax = \"proto3\"; package safety.testing; service Greeter { rpc " + rpc
               + " (R) returns (R) " + (body.Length == 0 ? ";" : body) + " }";
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

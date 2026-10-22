using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class RetryWithoutDeadlineTests
{
    [Theory]
    [InlineData("SayHello(new HelloRequest())")]
    [InlineData("SayHelloAsync(new HelloRequest())")]
    [InlineData("ListHello(new HelloRequest())")]
    [InlineData("UploadHello()")]
    [InlineData("StreamHello()")]
    [InlineData("SayHello(new HelloRequest(), deadline: null)")]
    [InlineData("SayHello(new HelloRequest(), deadline: DateTime.MaxValue)")]
    [InlineData("SayHello(new HelloRequest(), options: new CallOptions())")]
    [InlineData("SayHello(new HelloRequest(), options: new CallOptions().WithDeadline(DateTime.MaxValue))")]
    public async Task ConfirmedRetryAndAbsentDeadlineReportGrpc004(string invocation)
    {
        // Arrange
        var test = CreateTest("{|#0:" + RetryCallSource.Client() + "." + invocation + "|};");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.RetryWithoutDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("SayHello(new HelloRequest(), deadline: new DateTime(2030, 1, 1))")]
    [InlineData("SayHello(new HelloRequest(), options: new CallOptions(deadline: new DateTime(2030, 1, 1)))")]
    [InlineData("SayHello(new HelloRequest(), deadline: DateTime.UtcNow.AddSeconds(5))")]
    [InlineData("SayHello(new HelloRequest(), deadline: deadline)")]
    [InlineData("SayHello(new HelloRequest(), options: options)")]
    [InlineData("SayHello(new HelloRequest(), options: options.WithDeadline(DateTime.MaxValue))")]
    [InlineData("SayHello(new HelloRequest(), options: new CallOptions(propagationToken: token))")]
    public async Task ProtectedOrUnknownDeadlineDoesNotReport(string invocation)
    {
        // Arrange
        var test = CreateTest(
            RetryCallSource.Client() + "." + invocation + ";",
            "DateTime? deadline, CallOptions options, ContextPropagationToken token");
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HedgingRequiresOverallDeadlineInBothModes(bool strict)
    {
        // Arrange
        var client = RetryCallSource.Client(RetryCallSource.DefaultMethod(RetryTestSource.Hedging));
        var test = CreateTest("{|#0:" + client + ".SayHello(new HelloRequest())|};");
        test.Configure("grpc_safety.strict = " + strict.ToString().ToLowerInvariant());
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.RetryWithoutDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(", MaxRetryAttempts = 1", DiagnosticIds.MissingDeadline)]
    [InlineData(", MaxRetryAttempts = 2", DiagnosticIds.RetryWithoutDeadline)]
    [InlineData(", MaxRetryAttempts = null", DiagnosticIds.RetryWithoutDeadline)]
    public async Task ChannelLimitDeterminesEffectiveAttempts(string settings, string diagnostic)
    {
        // Arrange
        var test = CreateTest(
            "{|#0:" + RetryCallSource.Client(settings: settings) + ".SayHello(new HelloRequest())|};");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(diagnostic, DiagnosticSeverity.Warning).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task StableChannelClientAndOptionsLocalsRetainRetryConfiguration()
    {
        // Arrange
        var body = $$"""
                     GrpcChannelOptions options = {{RetryCallSource.Options()}};
                     GrpcChannel channel = GrpcChannel.ForAddress("https://localhost", options);
                     Greeter.GreeterClient client = new Greeter.GreeterClient(channel);
                     Greeter.GreeterClient alias = client;
                     {|#0:alias.SayHello(new HelloRequest())|};
                     """;
        var test = CreateTest(body);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.RetryWithoutDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("options.MaxRetryAttempts = 1;")]
    [InlineData("options.ServiceConfig.MethodConfigs.Clear();")]
    [InlineData("GrpcChannelOptions alias = options;")]
    public async Task MutatedOrAliasedOptionsRemainUnknown(string mutation)
    {
        // Arrange
        var body = $$"""
                     GrpcChannelOptions options = {{RetryCallSource.Options()}};
                     {{mutation}}
                     new Greeter.GreeterClient(GrpcChannel.ForAddress("https://localhost", options)).SayHello(new HelloRequest());
                     """;
        var test = CreateTest(body);
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(", MaxRetryAttempts = limit")]
    [InlineData(", MaxRetryAttempts = 0")]
    public async Task UnknownOrInvalidChannelLimitDoesNotReport(string settings)
    {
        // Arrange
        var test = CreateTest(
            RetryCallSource.Client(settings: settings) + ".SayHello(new HelloRequest());",
            "int limit");
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task OpaqueChannelConfigurationDoesNotReport()
    {
        // Arrange
        var test = CreateTest(
            "new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\", options)).SayHello(new HelloRequest());",
            "GrpcChannelOptions options");
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task ConditionalAccessToKnownClientRetainsRetryEvidence()
    {
        // Arrange
        var body = "Greeter.GreeterClient client = " + RetryCallSource.Client()
                                                     + "; client?{|#0:.SayHello(new HelloRequest())|};";
        var test = CreateTest(body);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.RetryWithoutDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("channel = other;")]
    [InlineData("ref GrpcChannel alias = ref channel; alias = other;")]
    [InlineData("Action replace = () => channel = other; replace();")]
    public async Task ReassignedChannelDoesNotProveRetryPolicy(string mutation)
    {
        // Arrange
        var body = $$"""
                     GrpcChannel channel = GrpcChannel.ForAddress("https://localhost", {{RetryCallSource.Options()}});
                     {{mutation}}
                     new Greeter.GreeterClient(channel).SayHello(new HelloRequest());
                     """;
        var test = CreateTest(body, "GrpcChannel other");
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnknownPolicyDoesNotProveMultipleAttempts(bool hedging)
    {
        // Arrange
        var policy = hedging
            ? "new HedgingPolicy { MaxAttempts = attempts }"
            : "new RetryPolicy { MaxAttempts = attempts }";
        var client = RetryCallSource.Client(RetryCallSource.DefaultMethod(policy));
        var test = CreateTest("{|#0:" + client + ".SayHello(new HelloRequest())|};", "int attempts");
        test.Configure("grpc_safety.strict = true");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task MalformedProtoDoesNotSuppressKnownRetryDeadlineViolation()
    {
        // Arrange
        var test = CreateTest("{|#0:" + RetryCallSource.Client() + ".SayHello(new HelloRequest())|};");
        test.TestState.AdditionalFiles.Add(("broken.proto", "service Broken { rpc"));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.RetryWithoutDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    private static GrpcAnalyzerTest CreateTest(string body, string parameters = "")
    {
        return new GrpcAnalyzerTest { TestCode = RetryCallSource.Wrap(body, parameters) };
    }
}

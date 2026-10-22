using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class RetryMethodSelectionTests
{
    private const string ExactName = "new MethodName { Service = \"safety.testing.Greeter\", Method = \"SayHello\" }";
    private const string ServiceName = "new MethodName { Service = \"safety.testing.Greeter\" }";

    [Theory]
    [InlineData(ExactName, true)]
    [InlineData(ServiceName, true)]
    [InlineData("new MethodName { Service = \"other.Greeter\", Method = \"SayHello\" }", false)]
    [InlineData("new MethodName { Service = \"safety.testing.Greeter\", Method = \"ReadHello\" }", false)]
    [InlineData("new MethodName { Service = \"safety.testing.OtherGreeter\" }", false)]
    public async Task SourceGeneratedClientUsesActualWireIdentity(string name, bool retry)
    {
        // Arrange
        var method = RetryTestSource.Method(name: name);
        var test = CreateTest(method);
        AddExpectedCall(test, retry);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("SayHello(new HelloRequest())")]
    [InlineData("SayHelloAsync(new HelloRequest())")]
    [InlineData("SayHello(new HelloRequest(), options: new CallOptions())")]
    [InlineData("SayHelloAsync(new HelloRequest(), options: new CallOptions())")]
    public async Task UnaryOverloadsResolveToSameRpc(string invocation)
    {
        // Arrange
        var client = RetryCallSource.Client(RetryTestSource.Method());
        var test = new GrpcAnalyzerTest
            { TestCode = RetryCallSource.Wrap("{|#0:" + client + "." + invocation + "|};") };
        test.AddGeneratedClientSource();
        AddExpectedCall(test, true);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MethodConfigurationOverridesServiceAndDefault(bool exactRetry)
    {
        // Arrange
        var broad = RetryTestSource.Method(name: ServiceName);
        var exact = exactRetry
            ? RetryTestSource.Method(name: ExactName)
            : "new MethodConfig { Names = { " + ExactName + " } }";
        var test = CreateTest(RetryCallSource.DefaultMethod() + ", " + broad + ", " + exact);
        AddExpectedCall(test, exactRetry);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task ServiceConfigurationWithoutRetryOverridesDefault()
    {
        // Arrange
        var service = "new MethodConfig { Names = { " + ServiceName + " } }";
        var test = CreateTest(RetryCallSource.DefaultMethod() + ", " + service);
        AddExpectedCall(test, false);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task UnknownSpecificPolicyDoesNotFallBackToDefaultRetry()
    {
        // Arrange
        var test = CreateTest(
            RetryCallSource.DefaultMethod() + ", "
                                            + RetryTestSource.Method("new RetryPolicy { MaxAttempts = 3 }"));
        AddExpectedCall(test, false);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task MetadataOnlyClientWithSpecificOverridesStaysUnknown()
    {
        // Arrange
        var test = CreateTest(
            RetryCallSource.DefaultMethod() + ", new MethodConfig { Names = { " + ExactName + " } }",
            false);
        AddExpectedCall(test, false);
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task DuplicateSelectorsMakeChannelConfigurationUnknown()
    {
        // Arrange
        var method = RetryTestSource.Method();
        var test = CreateTest(method + ", " + method);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task Grpc003AndGrpc004ReportOnTheirOwnLocations()
    {
        // Arrange
        var test = CreateTest("{|#1:" + RetryTestSource.Method() + "|}");
        test.TestState.AdditionalFiles.Add(("contract.proto", RetryTestSource.Proto));
        AddExpectedCall(test, true);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(1));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    private static GrpcAnalyzerTest CreateTest(string methods, bool source = true)
    {
        var test = new GrpcAnalyzerTest
        {
            TestCode = RetryCallSource.Wrap(
                "{|#0:" + RetryCallSource.Client(methods) + ".SayHello(new HelloRequest())|};")
        };
        if (source) test.AddGeneratedClientSource();
        return test;
    }

    private static void AddExpectedCall(GrpcAnalyzerTest test, bool retry)
    {
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(
                retry ? DiagnosticIds.RetryWithoutDeadline : DiagnosticIds.MissingDeadline,
                DiagnosticSeverity.Warning).WithLocation(0));
    }
}

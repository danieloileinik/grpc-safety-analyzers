using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class StrictModeTests
{
    [Fact]
    public async Task UnresolvedClientReportsInStrictMode()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap("{|#0:client.SayHello(new HelloRequest())|};", "Greeter.GreeterClient client"),
        };
        test.Configure("grpc_safety.strict = true");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("false")]
    [InlineData("invalid")]
    public async Task DisabledOrInvalidStrictOptionDoesNotReport(string value)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap("client.SayHello(new HelloRequest());", "Greeter.GreeterClient client"),
        };
        test.Configure($"grpc_safety.strict = {value}");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("client.SayHello(new HelloRequest(), deadline: deadline);", "Greeter.GreeterClient client, DateTime? deadline")]
    [InlineData("client.SayHello(new HelloRequest(), deadline: new DateTime(2030, 1, 1));", "Greeter.GreeterClient client")]
    [InlineData("client.SayHello(new HelloRequest(), options: options);", "Greeter.GreeterClient client, CallOptions options")]
    [InlineData("new Greeter.GreeterClient(invoker).SayHello(new HelloRequest());", "CallInvoker invoker")]
    [InlineData("new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\", options)).SayHello(new HelloRequest());", "GrpcChannelOptions options")]
    public async Task StrictModePreservesOtherUnknownEvidence(string body, string parameters)
    {
        // Arrange
        var test = new GrpcAnalyzerTest { TestCode = GrpcTestSource.Wrap(body, parameters) };
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }
}

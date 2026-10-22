using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class RetryClientFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ResolvedInjectedClientRequiresOverallDeadline(bool hedging)
    {
        // Arrange
        var test = CreateTest("{|#0:client.SayHello(new HelloRequest())|};", hedging: hedging);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.RetryWithoutDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(".EnableCallContextPropagation()")]
    [InlineData(".EnableCallContextPropagation(options => options.SuppressContextNotFoundErrors = true)")]
    public async Task CentralDeadlinePropagationProtectsRetriedCall(string extension)
    {
        // Arrange
        var test = CreateTest("client.SayHello(new HelloRequest());", extension);

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(".AddInterceptor<OpaqueInterceptor>()")]
    [InlineData(".ConfigureChannel(options => Change(options))")]
    public async Task OpaqueRegistrationDoesNotReport(string extension)
    {
        // Arrange
        var test = CreateTest("client.SayHello(new HelloRequest());", extension);
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task ParentDeadlineProtectsRetriedCallInHandler()
    {
        // Arrange
        var test = CreateTest("client.SayHello(new HelloRequest());");
        test.TestState.Sources.RemoveAt(0);
        test.TestCode = "using Grpc.Net.Client.Configuration;\n" + GrpcHandlerSource.Wrap(
            "client.SayHello(request, deadline: context.Deadline);",
            constructor: "(Greeter.GreeterClient client)");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task MissingDeadlinePropagationHasPriorityOverRetryAndDeadline()
    {
        // Arrange
        var test = CreateTest("client.SayHello(new HelloRequest());");
        test.TestState.Sources.RemoveAt(0);
        test.TestCode = "using Grpc.Net.Client.Configuration;\n" + GrpcHandlerSource.Wrap(
            "{|#0:client.SayHello(request)|};",
            constructor: "(Greeter.GreeterClient client)");
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadlinePropagation).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task DisablingGrpc004DoesNotExposeSuppressedGrpc001()
    {
        // Arrange
        var test = CreateTest("client.SayHello(new HelloRequest());");
        test.Configure("dotnet_diagnostic.GRPC004.severity = none");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task DisablingGrpc002DoesNotExposeGrpc004OrGrpc001()
    {
        // Arrange
        var test = CreateTest("client.SayHello(new HelloRequest());");
        test.TestState.Sources.RemoveAt(0);
        test.TestCode = GrpcHandlerSource.Wrap(
            "client.SayHello(request);",
            constructor: "(Greeter.GreeterClient client)");
        test.Configure("dotnet_diagnostic.GRPC002.severity = none");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    private static GrpcAnalyzerTest CreateTest(string body, string extension = "", bool hedging = false)
    {
        var test = new GrpcAnalyzerTest
        {
            TestCode = $$"""
                         using GrpcSafety.Analyzers.Tests.Fixtures;

                         public sealed class Caller(Greeter.GreeterClient client)
                         {
                             public void Send()
                             {
                                 {{body}}
                             }
                         }
                         """
        };
        test.AddClientFactoryReferences();
        var registration = RetryTestSource.Wrap(
            RetryCallSource.DefaultMethod(hedging ? RetryTestSource.Hedging : RetryTestSource.Retry),
            factory: true);
        registration = registration.Replace(
            "services.AddGrpcClient<Greeter.GreeterClient>()",
            "services.AddGrpcClient<Greeter.GreeterClient>()" + extension);
        test.TestState.Sources.Add(("Registration.cs", registration));
        test.TestState.Sources.Add(
            ("OpaqueInterceptor.cs",
                "public sealed class OpaqueInterceptor : Grpc.Core.Interceptors.Interceptor { }"));
        return test;
    }
}

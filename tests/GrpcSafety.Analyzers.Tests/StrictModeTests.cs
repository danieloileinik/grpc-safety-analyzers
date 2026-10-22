using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class StrictModeTests
{
    [Theory]
    [InlineData("{|#0:client.SayHello(new HelloRequest())|};")]
    [InlineData("client?{|#0:.SayHello(new HelloRequest())|};")]
    [InlineData("Greeter.GreeterClient local = client; local?{|#0:.SayHello(new HelloRequest())|};")]
    [InlineData("((Greeter.GreeterClient)client)?{|#0:.SayHello(new HelloRequest())|};")]
    public async Task UnresolvedClientReportsInStrictMode(string body)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                body,
                "Greeter.GreeterClient client")
        };
        test.Configure("grpc_safety.strict = true");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task OverriddenCallInvokerMethodReportsInStrictMode()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                "{|#0:invoker.BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest())|};",
                "CustomInvoker invoker, Method<HelloRequest, HelloReply> method")
        };
        test.TestState.Sources.Add(
            ("CustomInvoker.cs", """
                                  using System;
                                  using Grpc.Core;

                                  public abstract class CustomInvoker : CallInvoker
                                  {
                                      public override TResponse BlockingUnaryCall<TRequest, TResponse>(
                                          Method<TRequest, TResponse> method,
                                          string host,
                                          CallOptions options,
                                          TRequest request)
                                      {
                                          throw new NotSupportedException();
                                      }
                                  }
                                  """));
        test.Configure("grpc_safety.strict = true");
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

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
            TestCode = GrpcTestSource.Wrap("client.SayHello(new HelloRequest());", "Greeter.GreeterClient client")
        };
        test.Configure($"grpc_safety.strict = {value}");

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData(
        "client.SayHello(new HelloRequest(), deadline: deadline);",
        "Greeter.GreeterClient client, DateTime? deadline")]
    [InlineData(
        "client.SayHello(new HelloRequest(), deadline: new DateTime(2030, 1, 1));",
        "Greeter.GreeterClient client")]
    [InlineData(
        "client.SayHello(new HelloRequest(), options: options);",
        "Greeter.GreeterClient client, CallOptions options")]
    [InlineData("new Greeter.GreeterClient(invoker).SayHello(new HelloRequest());", "CallInvoker invoker")]
    [InlineData(
        "new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\", options)).SayHello(new HelloRequest());",
        "GrpcChannelOptions options")]
    [InlineData(
        "client?.SayHello(new HelloRequest(), deadline: deadline);",
        "Greeter.GreeterClient client, DateTime? deadline")]
    [InlineData(
        "client?.SayHello(new HelloRequest(), deadline: new DateTime(2030, 1, 1));",
        "Greeter.GreeterClient client")]
    [InlineData(
        "new Greeter.GreeterClient(invoker)?.SayHello(new HelloRequest());",
        "CallInvoker invoker")]
    [InlineData(
        "new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\", options))?.SayHello(new HelloRequest());",
        "GrpcChannelOptions options")]
    public async Task StrictModePreservesOtherUnknownEvidence(string body, string parameters)
    {
        // Arrange
        var test = new GrpcAnalyzerTest { TestCode = GrpcTestSource.Wrap(body, parameters) };
        test.Configure("grpc_safety.strict = true");

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }
}

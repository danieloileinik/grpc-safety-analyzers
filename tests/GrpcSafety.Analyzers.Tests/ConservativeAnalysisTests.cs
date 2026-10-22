using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class ConservativeAnalysisTests
{
    [Theory]
    [InlineData("client.SayHello(new HelloRequest());", "Greeter.GreeterClient client")]
    [InlineData("client?.SayHello(new HelloRequest());", "Greeter.GreeterClient client")]
    [InlineData("new Greeter.GreeterClient(channel).SayHello(new HelloRequest());", "GrpcChannel channel")]
    [InlineData("new Greeter.GreeterClient(invoker).SayHello(new HelloRequest());", "CallInvoker invoker")]
    [InlineData(
        "new Greeter.GreeterClient(GrpcChannel.ForAddress(address)).SayHello(new HelloRequest());",
        "string address")]
    [InlineData(
        "new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\", options)).SayHello(new HelloRequest());",
        "GrpcChannelOptions options")]
    [InlineData(
        "new Greeter.GreeterClient(GrpcChannel.ForAddress(\"https://localhost\", new GrpcChannelOptions { MaxRetryAttempts = DateTime.UtcNow.Day })).SayHello(new HelloRequest());",
        "")]
    [InlineData(
        "invoker.BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest());",
        "CallInvoker invoker, Method<HelloRequest, HelloReply> method")]
    public async Task UnresolvedConfigurationDoesNotReport(string body, string parameters)
    {
        // Arrange
        var test = new GrpcAnalyzerTest { TestCode = GrpcTestSource.Wrap(body, parameters) };

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task UnrelatedCallInvokerOverloadDoesNotReportInStrictMode()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                "invoker.BlockingUnaryCall(new CallOptions());",
                "CustomInvoker invoker")
        };
        test.TestState.Sources.Add(
            ("CustomInvoker.cs", """
                                 using Grpc.Core;

                                 public abstract class CustomInvoker : CallInvoker
                                 {
                                     public string BlockingUnaryCall(CallOptions options) => "cache lookup";
                                 }
                                 """));
        test.Configure("grpc_safety.strict = true");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task SimilarMethodNameOnUnrelatedTypeDoesNotReport()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap("new Pretend.GreeterClient().SayHello(new HelloRequest());")
        };
        test.TestState.Sources.Add(
            ("PretendClient.cs", """
                                 using System;

                                 namespace Pretend;

                                 public sealed class GreeterClient
                                 {
                                     public void SayHello(object request, DateTime? deadline = null) { }
                                 }
                                 """));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task CompilationWithoutGrpcIsIgnored()
    {
        // Arrange
        var test = new GrpcAnalyzerTest { TestCode = "public sealed class Empty { }" };
        test.TestState.AdditionalReferences.Clear();

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task DerivedClientConstructorDoesNotProveChannelConfiguration()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                "new CustomClient(GrpcChannel.ForAddress(\"https://localhost\")).SayHello(new HelloRequest());")
        };
        test.TestState.Sources.Add(
            ("CustomClient.cs", """
                                using Grpc.Core;
                                using GrpcSafety.Analyzers.Tests.Fixtures;

                                public sealed class CustomClient(ChannelBase channel) : Greeter.GreeterClient(channel);
                                """));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }
}

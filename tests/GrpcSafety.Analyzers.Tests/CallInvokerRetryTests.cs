using System;
using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class CallInvokerRetryTests
{
    private const string Method = """
                                  new Method<HelloRequest, HelloReply>(MethodType.Unary, "safety.testing.Greeter", "SayHello",
                                      Marshallers.Create<HelloRequest>(value => Array.Empty<byte>(), bytes => new HelloRequest()),
                                      Marshallers.Create<HelloReply>(value => Array.Empty<byte>(), bytes => new HelloReply()))
                                  """;

    [Theory]
    [InlineData("BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest())")]
    [InlineData("AsyncUnaryCall(method, null, new CallOptions(), new HelloRequest())")]
    [InlineData("AsyncServerStreamingCall(method, null, new CallOptions(), new HelloRequest())")]
    [InlineData("AsyncClientStreamingCall(method, null, new CallOptions())")]
    [InlineData("AsyncDuplexStreamingCall(method, null, new CallOptions())")]
    public async Task DirectInvokerUsesMatchingPolicy(string call)
    {
        // Arrange
        var methodType = call.StartsWith("AsyncServerStreamingCall", StringComparison.Ordinal) ? "ServerStreaming"
            : call.StartsWith("AsyncClientStreamingCall", StringComparison.Ordinal) ? "ClientStreaming"
            : call.StartsWith("AsyncDuplexStreamingCall", StringComparison.Ordinal) ? "DuplexStreaming" : "Unary";
        var descriptor = Method.Replace("MethodType.Unary", "MethodType." + methodType);
        var body = $$"""
                     Method<HelloRequest, HelloReply> method = {{descriptor}};
                     {|#0:GrpcChannel.ForAddress("https://localhost", {{RetryCallSource.Options("{|#1:" + RetryTestSource.Method() + "|}")}}).CreateCallInvoker().{{call}}|};
                     """;
        var test = new GrpcAnalyzerTest { TestCode = RetryCallSource.Wrap(body) };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.RetryWithoutDeadline).WithLocation(0));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(1));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task OpaqueMethodCanStillUseUnambiguousDefaultPolicy()
    {
        // Arrange
        var body = "{|#0:GrpcChannel.ForAddress(\"https://localhost\", " + RetryCallSource.Options()
                                                                         + ").CreateCallInvoker().BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest())|};";
        var test = new GrpcAnalyzerTest
            { TestCode = RetryCallSource.Wrap(body, "Method<HelloRequest, HelloReply> method") };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.RetryWithoutDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task OpaqueMethodDoesNotProveSpecificPolicyMatches()
    {
        // Arrange
        var body =
            "{|#0:GrpcChannel.ForAddress(\"https://localhost\", " + RetryCallSource.Options("{|#1:" + RetryTestSource.Method() + "|}")
                                                                  + ").CreateCallInvoker().BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest())|};";
        var test = new GrpcAnalyzerTest
            { TestCode = RetryCallSource.Wrap(body, "Method<HelloRequest, HelloReply> method") };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.UndeclaredIdempotency).WithLocation(1));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }
}

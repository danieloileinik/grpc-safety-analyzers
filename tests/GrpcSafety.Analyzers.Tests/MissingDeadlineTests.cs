using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class MissingDeadlineTests
{
    [Theory]
    [InlineData("")]
    [InlineData(", deadline: null")]
    [InlineData(", deadline: DateTime.MaxValue")]
    [InlineData(", deadline: default(DateTime?)")]
    public async Task DirectClientWithoutDeadlineReports(string argument)
    {
        // Arrange
        string call = $"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(){argument})";
        var test = new GrpcAnalyzerTest { TestCode = GrpcTestSource.Wrap($"{{|#0:{call}|}};") };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("default(CallOptions)")]
    [InlineData("new CallOptions()")]
    [InlineData("new CallOptions(deadline: null)")]
    [InlineData("new CallOptions().WithDeadline(DateTime.MaxValue)")]
    public async Task EmptyCallOptionsReport(string options)
    {
        // Arrange
        string call = $"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), options: {options})";
        var test = new GrpcAnalyzerTest { TestCode = GrpcTestSource.Wrap($"{{|#0:{call}|}};") };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("new CallOptions(deadline: new DateTime(2030, 1, 1))")]
    [InlineData("new CallOptions(deadline: previousDeadline)")]
    [InlineData("new CallOptions().WithDeadline(new DateTime(2030, 1, 1))")]
    [InlineData("new CallOptions().WithDeadline(previousDeadline.GetValueOrDefault())")]
    [InlineData("new CallOptions(propagationToken: null).WithDeadline(new DateTime(2030, 1, 1))")]
    public async Task ReplacingDeadlineWithMaxValueReportsWhenPropagationIsAbsent(string options)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                $"{{|#0:{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), options: {options}.WithDeadline(DateTime.MaxValue))|}};",
                "DateTime? previousDeadline")
        };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task ConditionalInlineClientWithoutDeadlineReports()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                $"{GrpcTestSource.DirectClient}?{{|#0:.SayHello(new HelloRequest())|}};")
        };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("new DateTime(2030, 1, 1)", "")]
    [InlineData("new DateTime(day: 1, year: 2030, month: 1)", "")]
    [InlineData("new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)", "")]
    [InlineData("new DateTime(10L)", "")]
    [InlineData("DateTime.MinValue", "")]
    [InlineData("default(DateTime)", "")]
    [InlineData("DateTime.UtcNow.AddSeconds(10)", "")]
    [InlineData("deadline", "DateTime? deadline")]
    [InlineData("new DateTime(ticks)", "long ticks")]
    [InlineData("new DateTime(2030, 2, 31)", "")]
    [InlineData("new DateTime(10L, (DateTimeKind)42)", "")]
    public async Task FiniteOrUnknownDeadlineDoesNotReport(string deadline, string parameters)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                $"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), deadline: {deadline});",
                parameters)
        };

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("new CallOptions(deadline: new DateTime(2030, 1, 1))", "")]
    [InlineData("new CallOptions().WithDeadline(new DateTime(2030, 1, 1))", "")]
    [InlineData("options", "CallOptions options")]
    [InlineData("options.WithDeadline(DateTime.MaxValue)", "CallOptions options")]
    [InlineData("new CallOptions(deadline: deadline)", "DateTime? deadline")]
    [InlineData("new CallOptions(propagationToken: token)", "ContextPropagationToken token")]
    [InlineData("new CallOptions(deadline: null, propagationToken: token)", "ContextPropagationToken token")]
    [InlineData("new CallOptions(deadline: new DateTime(2030, 1, 1), propagationToken: token).WithDeadline(DateTime.MaxValue)", "ContextPropagationToken token")]
    [InlineData("new CallOptions(propagationToken: token).WithDeadline(new DateTime(2030, 1, 1)).WithDeadline(DateTime.MaxValue)", "ContextPropagationToken token")]
    [InlineData("options.WithDeadline(new DateTime(2030, 1, 1)).WithDeadline(DateTime.MaxValue)", "CallOptions options")]
    public async Task ProtectedOrUnknownCallOptionsDoNotReport(string options, string parameters)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                $"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), options: {options});",
                parameters)
        };

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Theory]
    [InlineData("BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest())")]
    [InlineData("AsyncUnaryCall(method, null, new CallOptions(), new HelloRequest())")]
    [InlineData("AsyncServerStreamingCall(method, null, new CallOptions(), new HelloRequest())")]
    [InlineData("AsyncClientStreamingCall(method, null, new CallOptions())")]
    [InlineData("AsyncDuplexStreamingCall(method, null, new CallOptions())")]
    public async Task DirectCallInvokerWithoutDeadlineReports(string call)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                $$"""
                {|#0:GrpcChannel.ForAddress("https://localhost").CreateCallInvoker()
                    .{{call}}|};
                """,
                "Method<HelloRequest, HelloReply> method")
        };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task ConditionalInlineCallInvokerWithoutDeadlineReports()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                """
                GrpcChannel.ForAddress("https://localhost")?{|#0:.CreateCallInvoker()
                    .BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest())|};
                """,
                "Method<HelloRequest, HelloReply> method")
        };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        Task verification = test.RunAsync();

        // Assert
        await verification;
    }
}

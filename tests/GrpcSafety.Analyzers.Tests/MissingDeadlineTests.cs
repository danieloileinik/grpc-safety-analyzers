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
        var call = $"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(){argument})";
        var test = new GrpcAnalyzerTest { TestCode = GrpcTestSource.Wrap($"{{|#0:{call}|}};") };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

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
        var call = $"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), options: {options})";
        var test = new GrpcAnalyzerTest { TestCode = GrpcTestSource.Wrap($"{{|#0:{call}|}};") };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

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
                $"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), deadline: {deadline});", parameters),
        };

        // Act
        var verification = test.RunAsync();

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
    public async Task ProtectedOrUnknownCallOptionsDoNotReport(string options, string parameters)
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap(
                $"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), options: {options});", parameters),
        };

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task DirectCallInvokerWithoutDeadlineReports()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap("""
                {|#0:GrpcChannel.ForAddress("https://localhost").CreateCallInvoker()
                    .BlockingUnaryCall(method, null, new CallOptions(), new HelloRequest())|};
                """, "Method<HelloRequest, HelloReply> method"),
        };
        test.ExpectedDiagnostics.Add(new DiagnosticResult(DiagnosticDescriptors.MissingDeadline).WithLocation(0));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }
}

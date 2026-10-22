using System.Threading.Tasks;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class EditorConfigTests
{
    [Fact]
    public async Task RuleSeverityCanBeDisabled()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap($"{GrpcTestSource.DirectClient}.SayHello(new HelloRequest());")
        };
        test.Configure("dotnet_diagnostic.GRPC001.severity = none");

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }

    [Fact]
    public async Task CompilationOptionsCanRaiseSeverityToError()
    {
        // Arrange
        var test = new GrpcAnalyzerTest
        {
            TestCode = GrpcTestSource.Wrap($"{{|#0:{GrpcTestSource.DirectClient}.SayHello(new HelloRequest())|}};")
        };
        test.SolutionTransforms.Add((solution, projectId) =>
        {
            var options = solution.GetProject(projectId)!.CompilationOptions!;
            return solution.WithProjectCompilationOptions(
                projectId,
                options.WithSpecificDiagnosticOptions(
                    options.SpecificDiagnosticOptions.SetItem(DiagnosticIds.MissingDeadline, ReportDiagnostic.Error)));
        });
        test.ExpectedDiagnostics.Add(
            new DiagnosticResult(DiagnosticDescriptors.MissingDeadline)
                .WithLocation(0)
                .WithSeverity(DiagnosticSeverity.Error));

        // Act
        var verification = test.RunAsync();

        // Assert
        await verification;
    }
}

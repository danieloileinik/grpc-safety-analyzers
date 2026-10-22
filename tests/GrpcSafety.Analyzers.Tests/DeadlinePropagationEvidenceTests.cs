using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Configuration;
using GrpcSafety.Analyzers.Semantics;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class DeadlinePropagationEvidenceTests
{
    [Theory]
    [InlineData("", "context.Deadline")]
    [InlineData("ServerCallContext parent = context;", "parent.Deadline")]
    [InlineData("DateTime inherited = context.Deadline;", "inherited")]
    [InlineData("", "context.Deadline.AddTicks(-1)")]
    [InlineData("", "context.Deadline.AddSeconds(-2)")]
    [InlineData("", "context.Deadline.AddMinutes(0)")]
    [InlineData("", "context.Deadline.AddMilliseconds(-1)")]
    [InlineData("", "context.Deadline.AddDays(-1)")]
    [InlineData("", "context.Deadline.AddMonths(-1)")]
    [InlineData("", "context.Deadline.AddYears(-1)")]
    [InlineData("const int offset = -1;", "context.Deadline.AddSeconds(offset)")]
    [InlineData("", "context.Deadline.Subtract(TimeSpan.FromSeconds(1))")]
    [InlineData("", "context.Deadline.Add(TimeSpan.FromSeconds(-1))")]
    [InlineData("", "context.Deadline - TimeSpan.FromSeconds(1)")]
    [InlineData("", "context.Deadline + TimeSpan.FromTicks(-1)")]
    [InlineData("TimeSpan offset = TimeSpan.FromSeconds(1);", "context.Deadline - offset")]
    [InlineData("", "context.Deadline - new TimeSpan(1L)")]
    [InlineData("DateTime own = DateTime.UtcNow.AddSeconds(5);", "own < context.Deadline ? own : context.Deadline")]
    [InlineData("DateTime own = DateTime.UtcNow.AddSeconds(5);", "context.Deadline <= own ? context.Deadline : own")]
    [InlineData("DateTime own = DateTime.UtcNow.AddSeconds(5);", "own > context.Deadline ? context.Deadline : own")]
    [InlineData("DateTime own = DateTime.UtcNow.AddSeconds(5);", "context.Deadline >= own ? own : context.Deadline")]
    [InlineData("", "new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)")]
    public async Task InheritedOrFiniteDeadlineIsProtected(string setup, string deadline)
    {
        // Arrange
        var source = GrpcHandlerSource.Wrap(
            $"{setup}\n{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), deadline: {deadline});");

        // Act
        var classification = await ClassifyAsync(source);

        // Assert
        Assert.Equal(ProtectionState.Protected, classification.Deadline);
        Assert.Equal(ProtectionState.Protected, classification.DeadlinePropagation);
        Assert.Null(classification.Diagnostic);
    }

    [Theory]
    [InlineData("", "new CallOptions(deadline: context.Deadline)")]
    [InlineData("", "new CallOptions().WithDeadline(context.Deadline)")]
    [InlineData("", "new CallOptions().WithDeadline(context.Deadline).WithCancellationToken(CancellationToken.None)")]
    [InlineData("", "new CallOptions().WithDeadline(context.Deadline).WithHeaders(new Metadata())")]
    [InlineData("CallOptions options = new CallOptions(deadline: context.Deadline);", "options")]
    [InlineData("CallOptions options = new CallOptions();", "options.WithDeadline(context.Deadline)")]
    [InlineData(
        "DateTime deadline = context.Deadline.AddSeconds(-1); CallOptions options = new CallOptions(deadline: deadline);",
        "options")]
    [InlineData(
        "CallOptions first = new CallOptions(deadline: context.Deadline); CallOptions second = first;",
        "second")]
    public async Task CallOptionsPreserveInheritedDeadline(string setup, string options)
    {
        // Arrange
        var source = GrpcHandlerSource.Wrap(
            $"{setup}\n{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), options: {options});");

        // Act
        var classification = await ClassifyAsync(source);

        // Assert
        Assert.Equal(ProtectionState.Protected, classification.DeadlinePropagation);
        Assert.Null(classification.Diagnostic);
    }

    [Theory]
    [InlineData("", "DateTime.UtcNow.AddSeconds(5)")]
    [InlineData("", "context.Deadline.AddSeconds(1)")]
    [InlineData("", "context.Deadline.AddSeconds(double.NaN)")]
    [InlineData("", "context.Deadline.AddSeconds(double.NegativeInfinity)")]
    [InlineData("", "context.Deadline + TimeSpan.FromSeconds(1)")]
    [InlineData("", "context.Deadline - TimeSpan.FromSeconds(-1)")]
    [InlineData("DateTime deadline = context.Deadline; deadline = DateTime.MaxValue;", "deadline")]
    [InlineData(
        "DateTime deadline = DateTime.MaxValue; ref DateTime alias = ref deadline; alias = context.Deadline;",
        "deadline")]
    [InlineData(
        "DateTime deadline = DateTime.MaxValue; Action mutate = () => deadline = context.Deadline; mutate();",
        "deadline")]
    [InlineData("DateTime own = DateTime.UtcNow;", "own > context.Deadline ? own : context.Deadline")]
    [InlineData("DateTime? own = null;", "context.Deadline <= own ? context.Deadline : own")]
    [InlineData("", "DateTime.UtcNow < context.Deadline ? DateTime.UtcNow : context.Deadline")]
    [InlineData("ServerCallContext other = null;", "other.Deadline")]
    public async Task UnresolvedDeadlineRemainsUnknown(string setup, string deadline)
    {
        // Arrange
        var source = GrpcHandlerSource.Wrap(
            $"{setup}\n{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), deadline: {deadline});");

        // Act
        var classification = await ClassifyAsync(source);

        // Assert
        Assert.Equal(ProtectionState.Unknown, classification.Deadline);
        Assert.Equal(ProtectionState.Unknown, classification.DeadlinePropagation);
        Assert.Null(classification.Diagnostic);
    }

    [Theory]
    [InlineData("CallOptions options = new CallOptions(); options = new CallOptions(deadline: context.Deadline);")]
    [InlineData(
        "CallOptions options = new CallOptions(); void Mutate(ref CallOptions value) { value = new CallOptions(deadline: context.Deadline); } Mutate(ref options);")]
    [InlineData(
        "CallOptions options = new CallOptions(); Action mutate = () => options = new CallOptions(deadline: context.Deadline); mutate();")]
    [InlineData(
        "CallOptions options = new CallOptions(); ref CallOptions alias = ref options; alias = new CallOptions(deadline: context.Deadline);")]
    public async Task MutableCallOptionsRemainUnknown(string setup)
    {
        // Arrange
        var source = GrpcHandlerSource.Wrap(
            $"{setup}\n{GrpcTestSource.DirectClient}.SayHello(new HelloRequest(), options: options);");

        // Act
        var classification = await ClassifyAsync(source);

        // Assert
        Assert.Equal(ProtectionState.Unknown, classification.Deadline);
        Assert.Null(classification.Diagnostic);
    }

    private static async Task<CallSafetyClassification> ClassifyAsync(string source)
    {
        var test = new GrpcAnalyzerTest();
        var references = await test.ReferenceAssemblies.ResolveAsync(LanguageNames.CSharp, CancellationToken.None);
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(
            "DeadlinePropagationEvidence",
            [tree],
            references.AddRange(test.TestState.AdditionalReferences),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.DoesNotContain(
            compilation.GetDiagnostics(),
            diagnostic => diagnostic.Severity is DiagnosticSeverity.Error);
        var model = compilation.GetSemanticModel(tree);
        var symbols = new GrpcSymbols(compilation);
        var recognizer = new GrpcCallRecognizer(symbols);
        foreach (var invocation in tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
            if (model.GetOperation(invocation) is IInvocationOperation operation
                && recognizer.TryRecognize(operation, out var call))
                return new CallAnalysisPipeline(symbols, []).Analyze(
                    call,
                    new GrpcSafetyOptions(false),
                    CancellationToken.None);

        throw new InvalidOperationException("The test source does not contain a recognized gRPC call.");
    }
}

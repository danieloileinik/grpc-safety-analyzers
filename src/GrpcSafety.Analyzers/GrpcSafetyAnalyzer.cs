using System.Collections.Immutable;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Configuration;
using GrpcSafety.Analyzers.Semantics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class GrpcSafetyAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => DiagnosticDescriptors.All;

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var symbols = new GrpcSymbols(start.Compilation);
            if (!symbols.IsAvailable) return;

            var recognizer = new GrpcCallRecognizer(symbols);
            var pipeline = new CallAnalysisPipeline(symbols, start.Options.AdditionalFiles);
            start.RegisterOperationAction(
                operation =>
                {
                    if (!recognizer.TryRecognize((IInvocationOperation)operation.Operation, out GrpcCall call)) return;

                    GrpcSafetyOptions options = GrpcSafetyOptions.Read(
                        operation.Options.AnalyzerConfigOptionsProvider,
                        call.Invocation.Syntax.SyntaxTree);
                    CallSafetyClassification classification = pipeline.Analyze(call, options, operation.CancellationToken);
                    if (classification.Diagnostic is { } diagnostic)
                        operation.ReportDiagnostic(Diagnostic.Create(diagnostic, call.Invocation.Syntax.GetLocation()));
                },
                OperationKind.Invocation);
        });
    }
}

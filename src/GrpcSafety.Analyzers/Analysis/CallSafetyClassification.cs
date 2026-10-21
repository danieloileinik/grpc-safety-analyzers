using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Analysis;

internal readonly struct CallSafetyClassification(
    ProtectionState deadline,
    ProtectionState propagation,
    ProtectionState retry,
    DiagnosticDescriptor? diagnostic)
{
    public ProtectionState Deadline { get; } = deadline;

    public ProtectionState Propagation { get; } = propagation;

    public ProtectionState Retry { get; } = retry;

    public DiagnosticDescriptor? Diagnostic { get; } = diagnostic;
}

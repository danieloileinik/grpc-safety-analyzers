using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Analysis;

internal readonly struct CallSafetyClassification(
    ProtectionState deadline,
    ProtectionState deadlinePropagation,
    ProtectionState retry,
    DiagnosticDescriptor? diagnostic)
{
    public ProtectionState Deadline { get; } = deadline;

    public ProtectionState DeadlinePropagation { get; } = deadlinePropagation;

    public ProtectionState Retry { get; } = retry;

    public DiagnosticDescriptor? Diagnostic { get; } = diagnostic;
}

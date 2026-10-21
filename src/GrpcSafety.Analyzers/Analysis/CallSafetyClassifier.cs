using GrpcSafety.Analyzers.Configuration;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Analysis;

internal static class CallSafetyClassifier
{
    public static CallSafetyClassification Classify(
        ProtectionState explicitDeadline,
        ClientConfiguration configuration,
        ProtectionState propagation,
        RetrySafety retry,
        GrpcSafetyOptions options)
    {
        var centralDeadline = !configuration.IsRegistrationResolved && options.Strict
            ? ProtectionState.Unprotected
            : configuration.CentralDeadline;

        var deadline = (explicitDeadline, centralDeadline) switch
        {
            (ProtectionState.Protected, _) or (_, ProtectionState.Protected) => ProtectionState.Protected,
            (ProtectionState.Unprotected, ProtectionState.Unprotected) => ProtectionState.Unprotected,
            _ => ProtectionState.Unknown,
        };

        if (deadline is ProtectionState.Protected)
        {
            propagation = ProtectionState.Protected;
        }

        // Select one diagnostic here; independent rule callbacks must not suppress each other.
        DiagnosticDescriptor? diagnostic = (propagation, deadline, retry.HasConfirmedMultipleAttempts, retry.State) switch
        {
            (ProtectionState.Unprotected, _, _, _) => DiagnosticDescriptors.MissingPropagation,
            (_, ProtectionState.Unprotected, true, _) => DiagnosticDescriptors.RetryWithoutDeadline,
            (_, ProtectionState.Unprotected, _, _) => DiagnosticDescriptors.MissingDeadline,
            (_, _, true, ProtectionState.Unprotected) => DiagnosticDescriptors.UndeclaredIdempotency,
            _ => null,
        };

        return new(deadline, propagation, retry.State, diagnostic);
    }
}

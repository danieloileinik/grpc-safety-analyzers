using GrpcSafety.Analyzers.Configuration;

namespace GrpcSafety.Analyzers.Analysis;

internal static class CallSafetyClassifier
{
    public static CallSafetyClassification Classify(
        ProtectionState explicitDeadline,
        ClientConfiguration configuration,
        ProtectionState deadlinePropagation,
        RetrySafety retry,
        GrpcSafetyOptions options)
    {
        var deadline = ClassifyDeadline(explicitDeadline, configuration, options);

        if (deadline is ProtectionState.Protected) deadlinePropagation = ProtectionState.Protected;

        // Select one diagnostic here; independent rule callbacks must not suppress each other.
        var diagnostic = (deadlinePropagation, deadline, retry.HasConfirmedMultipleAttempts) switch
        {
            (ProtectionState.Unprotected, _, _) => DiagnosticDescriptors.MissingDeadlinePropagation,
            (_, ProtectionState.Unprotected, true) => DiagnosticDescriptors.RetryWithoutDeadline,
            (_, ProtectionState.Unprotected, _) => DiagnosticDescriptors.MissingDeadline,
            (_, _, true) when retry.State is ProtectionState.Unprotected => DiagnosticDescriptors.UndeclaredIdempotency,
            _ => null
        };

        return new CallSafetyClassification(deadline, deadlinePropagation, retry.State, diagnostic);
    }

    public static ProtectionState ClassifyDeadline(
        ProtectionState explicitDeadline,
        ClientConfiguration configuration,
        GrpcSafetyOptions options)
    {
        var centralDeadline = !configuration.IsRegistrationResolved && options.Strict
            ? ProtectionState.Unprotected
            : configuration.CentralDeadline;

        return (explicitDeadline, centralDeadline) switch
        {
            (ProtectionState.Protected, _) or (_, ProtectionState.Protected) => ProtectionState.Protected,
            (ProtectionState.Unprotected, ProtectionState.Unprotected) => ProtectionState.Unprotected,
            _ => ProtectionState.Unknown
        };
    }
}

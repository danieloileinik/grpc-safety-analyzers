namespace GrpcSafety.Analyzers.Analysis;

internal readonly struct RetrySafety(ProtectionState state, bool hasConfirmedMultipleAttempts)
{
    // State describes potential RPC idempotency after the effective attempt count is resolved.
    public ProtectionState State { get; } = state;

    public bool HasConfirmedMultipleAttempts { get; } = hasConfirmedMultipleAttempts;

    public static RetrySafety Unknown => default;
}

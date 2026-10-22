using GrpcSafety.Analyzers.Analysis;

namespace GrpcSafety.Analyzers.Configuration;

internal readonly struct ClientConfiguration(
    bool isRegistrationResolved,
    ProtectionState centralDeadline,
    ChannelRetryConfiguration? retry = null)
{
    public bool IsRegistrationResolved { get; } = isRegistrationResolved;

    public ProtectionState CentralDeadline { get; } = centralDeadline;

    public ChannelRetryConfiguration? Retry { get; } = retry;

    public static ClientConfiguration Unresolved => default;

    public static ClientConfiguration Opaque => new(true, ProtectionState.Unknown);

    public static ClientConfiguration WithoutCentralDeadline => new(true, ProtectionState.Unprotected);
}

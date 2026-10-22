using GrpcSafety.Analyzers.Analysis;

namespace GrpcSafety.Analyzers.Configuration;

internal readonly struct ClientConfiguration(bool isRegistrationResolved, ProtectionState centralDeadline)
{
    public bool IsRegistrationResolved { get; } = isRegistrationResolved;

    public ProtectionState CentralDeadline { get; } = centralDeadline;

    public static ClientConfiguration Unresolved => default;

    public static ClientConfiguration Opaque => new ClientConfiguration(true, ProtectionState.Unknown);

    public static ClientConfiguration WithoutCentralDeadline => new ClientConfiguration(true, ProtectionState.Unprotected);
}

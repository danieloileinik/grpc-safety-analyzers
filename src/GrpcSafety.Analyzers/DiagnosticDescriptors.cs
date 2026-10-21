using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers;

internal static class DiagnosticDescriptors
{
    private const string Category = "GrpcSafety";

    public static readonly DiagnosticDescriptor MissingDeadline = Create(
        DiagnosticIds.MissingDeadline,
        "gRPC call has no deadline",
        "Set a deadline for this gRPC call",
        "The call has no deadline and its resolved client configuration provides no central deadline mechanism.");

    public static readonly DiagnosticDescriptor MissingPropagation = Create(
        DiagnosticIds.MissingPropagation,
        "gRPC call does not inherit the parent deadline",
        "Propagate the parent RPC deadline or set a finite deadline",
        "A call inside an RPC handler must inherit the parent deadline or have its own finite deadline.");

    public static readonly DiagnosticDescriptor UndeclaredIdempotency = Create(
        DiagnosticIds.UndeclaredIdempotency,
        "gRPC retries require declared idempotency",
        "Declare idempotency in the proto contract before enabling retries or hedging",
        "A confirmed retry or hedging policy permits multiple attempts for a method without declared idempotency.");

    public static readonly DiagnosticDescriptor RetryWithoutDeadline = Create(
        DiagnosticIds.RetryWithoutDeadline,
        "gRPC retries have no overall deadline",
        "Set an overall deadline for this retried gRPC call",
        "A confirmed retry or hedging policy permits multiple attempts without a deadline bounding their total duration.");

    public static ImmutableArray<DiagnosticDescriptor> All { get; } =
    [
        MissingDeadline,
        MissingPropagation,
        UndeclaredIdempotency,
        RetryWithoutDeadline,
    ];

    private static DiagnosticDescriptor Create(string id, string title, string message, string description) =>
        new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: description);
}

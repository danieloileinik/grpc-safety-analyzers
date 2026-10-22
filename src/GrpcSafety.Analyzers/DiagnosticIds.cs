namespace GrpcSafety.Analyzers;

internal static class DiagnosticIds
{
    public const string MissingDeadline = "GRPC001";
    public const string MissingDeadlinePropagation = "GRPC002";
    public const string UndeclaredIdempotency = "GRPC003";
    public const string RetryWithoutDeadline = "GRPC004";
}

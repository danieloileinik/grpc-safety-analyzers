using System;

namespace GrpcSafety.Analyzers.Analysis;

internal static class RpcIdempotencyClassifier
{
    public static ProtectionState Classify(string method, ProtectionState? declaredIdempotency = null)
    {
        var inferred = method switch
        {
            _ when StartsWith(method, "Get") || StartsWith(method, "List") || StartsWith(method, "Find") =>
                ProtectionState.Protected,
            _ when StartsWith(method, "Create") || StartsWith(method, "Delete") || StartsWith(method, "Increment")
                   || StartsWith(method, "Charge") || StartsWith(method, "Append") => ProtectionState.Unprotected,
            _ => ProtectionState.Unknown
        };

        return declaredIdempotency ?? inferred;
    }

    private static bool StartsWith(string method, string prefix)
    {
        return method.StartsWith(prefix, StringComparison.Ordinal);
    }
}

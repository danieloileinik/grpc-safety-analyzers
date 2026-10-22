namespace GrpcSafety.Analyzers.Configuration;

internal readonly struct MethodNamePattern(string? service, string? method)
{
    public string? Service { get; } = service;

    public string? Method { get; } = method;
}

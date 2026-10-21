namespace GrpcSafety.Analyzers.Protobuf;

internal readonly struct ProtoMethodIdentity(string service, string method)
{
    public string Service { get; } = service;

    public string Method { get; } = method;
}

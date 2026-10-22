namespace GrpcSafety.Analyzers.Tests.TestSupport;

internal static class GrpcHandlerSource
{
    public const string UnarySignature =
        "public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)";

    public static string Wrap(string body,
        string members = "",
        string signature = UnarySignature,
        string constructor = "")
    {
        return $$"""
                 using System;
                 using System.Threading;
                 using System.Threading.Tasks;
                 using Grpc.Core;
                 using Grpc.Net.Client;
                 using GrpcSafety.Analyzers.Tests.Fixtures;

                 public sealed class ForwardingService{{constructor}} : Greeter.GreeterBase
                 {
                     {{signature}}
                     {
                         {{body}}
                         return Task.FromResult(new HelloReply());
                     }

                     {{members}}
                 }
                 """;
    }
}

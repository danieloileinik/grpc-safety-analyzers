using System.IO;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcSafety.Analyzers.Tests.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;

namespace GrpcSafety.Analyzers.Tests.TestSupport;

internal sealed class GrpcAnalyzerTest : CSharpAnalyzerTest<GrpcSafetyAnalyzer, DefaultVerifier>
{
    public GrpcAnalyzerTest()
    {
        ReferenceAssemblies = new ReferenceAssemblies(
            "net10.0",
            new PackageIdentity("Microsoft.NETCore.App.Ref", "10.0.0"),
            Path.Combine("ref", "net10.0"));

        TestState.AdditionalReferences.AddRange(
        [
            MetadataReference.CreateFromFile(typeof(ClientBase).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GrpcChannel).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IMessage).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Greeter).Assembly.Location)
        ]);
    }

    public void Configure(string configuration)
    {
        TestState.AnalyzerConfigFiles.Add(
            ("/.editorconfig", $$"""
                                 root = true

                                 [*.cs]
                                 {{configuration}}
                                 """));
    }
}

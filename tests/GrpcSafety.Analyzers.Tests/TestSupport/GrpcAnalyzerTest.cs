using System;
using System.IO;
using Google.Protobuf;
using Grpc.AspNetCore.ClientFactory;
using Grpc.Core;
using Grpc.Net.Client;
using Grpc.Net.ClientFactory;
using GrpcSafety.Analyzers.Tests.Fixtures;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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

    public void AddClientFactoryReferences()
    {
        TestState.AdditionalReferences.AddRange(
        [
            MetadataReference.CreateFromFile(typeof(GrpcClientFactory).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GrpcContextPropagationOptions).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IHttpClientBuilder).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(ILoggerFactory).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IOptions<>).Assembly.Location)
        ]);
    }

    public void AddGeneratedClientSource()
    {
        TestState.Sources.Add(
            ("GreeterGrpc.g.cs", File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "GreeterGrpc.fixture.cs"))));
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

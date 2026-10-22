using System.Collections.Immutable;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Protobuf;
using GrpcSafety.Analyzers.Tests.TestSupport;
using Microsoft.CodeAnalysis;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class ProtoContractResolverTests
{
    [Theory]
    [InlineData("p.S", "M", (int)ProtectionState.Unprotected)]
    [InlineData("p.S", "Read", (int)ProtectionState.Protected)]
    [InlineData("S", "M", (int)ProtectionState.Unknown)]
    [InlineData("p.Missing", "M", (int)ProtectionState.Unknown)]
    [InlineData("p.S", "Missing", (int)ProtectionState.Unknown)]
    public void MatchesQualifiedProtoServiceAndExactMethod(string service, string method, int expected)
    {
        // Arrange
        var resolver = new ProtoContractResolver();
        ImmutableArray<AdditionalText> files =
        [
            new ProtoAdditionalText(
                "a.proto",
                "package p; service S { rpc M (R) returns (R); rpc Read (R) returns (R) { option idempotency_level = IDEMPOTENT; } }")
        ];

        // Act
        var state = resolver.ResolveIdempotency(new ProtoMethodIdentity(service, method), files, default);

        // Assert
        Assert.Equal((ProtectionState)expected, state);
    }

    [Theory]
    [InlineData("a.proto", null)]
    [InlineData("a.proto", "service S {")]
    [InlineData("a.proto", "package p; service S { rpc M (R) returns (R); }")]
    public void UnreadableMalformedOrDuplicateProtoMakesResolutionUnknown(string path, string? source)
    {
        // Arrange
        var resolver = new ProtoContractResolver();
        ImmutableArray<AdditionalText> files =
        [
            new ProtoAdditionalText("first.proto", "package p; service S { rpc M (R) returns (R); }"),
            new ProtoAdditionalText(path, source)
        ];

        // Act
        var state = resolver.ResolveIdempotency(new ProtoMethodIdentity("p.S", "M"), files, default);

        // Assert
        Assert.Equal(ProtectionState.Unknown, state);
    }

    [Fact]
    public void NonProtoFilesDoNotAffectResolution()
    {
        // Arrange
        var resolver = new ProtoContractResolver();
        ImmutableArray<AdditionalText> files =
        [
            new ProtoAdditionalText("first.PROTO", "service S { rpc M (R) returns (R); }"),
            new ProtoAdditionalText("settings.json", "not proto")
        ];

        // Act
        var state = resolver.ResolveIdempotency(new ProtoMethodIdentity("S", "M"), files, default);

        // Assert
        Assert.Equal(ProtectionState.Unprotected, state);
    }
}

using System;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Protobuf;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class ProtoParserTests
{
    [Theory]
    [InlineData(";", (int)ProtectionState.Unprotected)]
    [InlineData("{}", (int)ProtectionState.Unprotected)]
    [InlineData("{ option idempotency_level = IDEMPOTENT; }", (int)ProtectionState.Protected)]
    [InlineData("{ option idempotency_level = NO_SIDE_EFFECTS; }", (int)ProtectionState.Protected)]
    [InlineData("{ option idempotency_level = IDEMPOTENCY_UNKNOWN; }", (int)ProtectionState.Unprotected)]
    [InlineData("{ option idempotency_level = FUTURE; }", (int)ProtectionState.Unknown)]
    [InlineData("{ option idempotency_level = 2; }", (int)ProtectionState.Unknown)]
    [InlineData("{ option (custom) = true; }", (int)ProtectionState.Unknown)]
    [InlineData("{ option idempotency_level = IDEMPOTENT; option deprecated = true; }", (int)ProtectionState.Unknown)]
    [InlineData(
        "{ option idempotency_level = IDEMPOTENT; option idempotency_level = IDEMPOTENT; }",
        (int)ProtectionState.Unknown)]
    [InlineData("{ option (custom) = { idempotency_level: IDEMPOTENT }; }", (int)ProtectionState.Unknown)]
    public void ClassifiesOnlyKnownRpcOptions(string body, int expected)
    {
        // Arrange
        var source = "syntax = \"proto3\"; package p; service S { rpc M (Req) returns (Reply) " + body + " }";

        // Act
        var file = ProtoParser.Parse(source);

        // Assert
        Assert.True(file.IsComplete);
        Assert.Equal((ProtectionState)expected, Assert.Single(file.Services).Methods["M"]);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("stream", "")]
    [InlineData("", "stream")]
    [InlineData("stream", "stream")]
    public void SupportsCommentsPackagesStreamingAndUnrelatedMessages(string requestStream, string responseStream)
    {
        // Arrange
        var source = $$"""
                       // service Fake { rpc M (R) returns (R); }
                       syntax = 'proto3';
                       package example.testing;
                       import "request.proto";
                       option csharp_namespace = "Other.Namespace";
                       message Request { message Nested { string value = 1; } }
                       enum State { NONE = 0; }
                       /* option idempotency_level = IDEMPOTENT; */
                       service S {
                         rpc M ({{requestStream}} .example.Request) returns ({{responseStream}} Reply) {
                           option /* comment */ idempotency_level = NO_SIDE_EFFECTS;
                         }
                       }
                       """;

        // Act
        var file = ProtoParser.Parse(source);

        // Assert
        Assert.True(file.IsComplete);
        Assert.Equal("example.testing", file.Package);
        Assert.Equal(ProtectionState.Protected, Assert.Single(file.Services).Methods["M"]);
    }

    [Theory]
    [InlineData("syntax = \"proto4\";")]
    [InlineData("edition = \"2023\";")]
    [InlineData("package p; package p;")]
    [InlineData("package .p;")]
    [InlineData("package p")]
    [InlineData("/* unclosed")]
    [InlineData("option x = \"unclosed;")]
    [InlineData("option x = \"escaped\\\nnewline\";")]
    [InlineData("service S {")]
    [InlineData("service S { rpc M (R) returns (R)")]
    [InlineData("service S { rpc M (R) returns (R); rpc M (R) returns (R); }")]
    [InlineData("service S {} service S {}")]
    [InlineData("service S { rpc M (R) returns (R) { option idempotency_level = IDEMPOTENT } }")]
    [InlineData("service S { rpc M (R) returns (R) { option x = (]; } }")]
    [InlineData("service S { unknown; }")]
    [InlineData("service S { rpc M (R) returns (R); } @")]
    public void MalformedOrUnsupportedFileIsUnknown(string source)
    {
        // Arrange
        var text = source;

        // Act
        var file = ProtoParser.Parse(text);

        // Assert
        Assert.False(file.IsComplete);
    }

    [Fact]
    public void UnsupportedServiceOptionIsUnknown()
    {
        // Arrange
        const string source = "service S { option (custom) = true; rpc M (R) returns (R); }";

        // Act
        var file = ProtoParser.Parse(source);

        // Assert
        Assert.True(file.IsComplete);
        Assert.False(Assert.Single(file.Services).IsKnown);
    }

    [Fact]
    public void CancellationIsObserved()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        Action parse = () => ProtoParser.Parse("service S {}", cancellation.Token);

        // Assert
        Assert.Throws<OperationCanceledException>(parse);
    }
}

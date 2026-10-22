using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace GrpcSafety.Analyzers.Tests.TestSupport;

internal sealed class ProtoAdditionalText(string path, string? source) : AdditionalText
{
    public override string Path => path;

    public override SourceText? GetText(CancellationToken cancellationToken = default)
    {
        return source is null ? null : SourceText.From(source);
    }
}

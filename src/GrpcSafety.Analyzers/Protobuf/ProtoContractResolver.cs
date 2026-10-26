using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using Microsoft.CodeAnalysis;

namespace GrpcSafety.Analyzers.Protobuf;

internal sealed class ProtoContractResolver : IProtoContractResolver
{
    private readonly ConcurrentDictionary<AdditionalText, ProtoFile> _files = new();

    public ProtectionState ResolveIdempotency(ProtoMethodIdentity method,
        ImmutableArray<AdditionalText> additionalFiles,
        CancellationToken cancellationToken)
    {
        var service = FindService(method.Service, additionalFiles, cancellationToken);
        ProtectionState? declaration = service is not null && service.Methods.TryGetValue(method.Method, out var state)
            ? state
            : null;
        return RpcIdempotencyClassifier.Classify(method.Method, declaration);
    }

    public ProtoService? FindService(string fullName,
        ImmutableArray<AdditionalText> additionalFiles,
        CancellationToken cancellationToken)
    {
        ProtoService? found = null;
        foreach (var text in additionalFiles.Where(file => file.Path.EndsWith(
                     ".proto",
                     StringComparison.OrdinalIgnoreCase)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = _files.GetOrAdd(text, source => ReadFile(source, cancellationToken));
            if (!file.IsComplete) return null;
            foreach (var service in file.Services)
            {
                var name = file.Package.Length == 0 ? service.Name : file.Package + "." + service.Name;
                if (name != fullName) continue;
                if (found is not null || !service.IsKnown) return null;
                found = service;
            }
        }

        return found;
    }

    private static ProtoFile ReadFile(AdditionalText text, CancellationToken cancellationToken)
    {
        try
        {
            var source = text.GetText(cancellationToken);
            return source is null ? ProtoFile.Unknown : ProtoParser.Parse(source.ToString(), cancellationToken);
        }
        catch (IOException)
        {
            return ProtoFile.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return ProtoFile.Unknown;
        }
    }
}

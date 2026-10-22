using System;
using System.Linq;
using System.Threading;
using GrpcSafety.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class DeadlineResolver(GrpcSymbols symbols)
{
    private readonly RpcHandlerRecognizer _handlers = new(symbols);
    private readonly LocalValueResolver _locals = new(symbols.Compilation);

    public ProtectionState Resolve(GrpcCall call, CancellationToken cancellationToken = default)
    {
        var context = _handlers.FindContext(call, cancellationToken);
        var arguments = call.Invocation.Arguments;
        var deadline = arguments.FirstOrDefault(argument =>
            argument.Parameter is { Name: "deadline" } parameter && GrpcSymbols.IsNullableDateTime(parameter.Type));
        if (deadline is not null) return ResolveDateTime(deadline.Value, context, cancellationToken);

        var options =
            arguments.FirstOrDefault(argument => GrpcSymbols.Is(argument.Parameter?.Type, symbols.CallOptions));
        return options is null
            ? ProtectionState.Unknown
            : ResolveCallOptions(options.Value, context, cancellationToken);
    }

    internal static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { OperatorMethod: null } conversion) operation = conversion.Operand;

        return operation;
    }

    private ProtectionState ResolveCallOptions(
        IOperation operation,
        IParameterSymbol? context,
        CancellationToken cancellationToken,
        int depth = 0)
    {
        if (depth > 32) return ProtectionState.Unknown;
        operation = _locals.Resolve(operation, cancellationToken);
        if (operation is IDefaultValueOperation) return ProtectionState.Unprotected;

        if (operation is IInvocationOperation { TargetMethod.Name: "WithDeadline" } invocation
            && GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.CallOptions)
            && invocation.Arguments.Length is 1)
        {
            var deadline = ResolveDateTime(invocation.Arguments[0].Value, context, cancellationToken);
            if (deadline is ProtectionState.Protected) return deadline;

            // An opaque CallOptions instance may carry a ContextPropagationToken that supplies a deadline.
            return deadline is ProtectionState.Unprotected
                   && invocation.Instance is not null
                   && HasKnownAbsentContextPropagationToken(invocation.Instance, cancellationToken)
                ? ProtectionState.Unprotected
                : ProtectionState.Unknown;
        }

        if (operation is IInvocationOperation { Instance: { } receiver } preserving
            && GrpcSymbols.Is(preserving.TargetMethod.ContainingType, symbols.CallOptions)
            && preserving.TargetMethod.Name is "WithCancellationToken" or "WithHeaders" or "WithWriteOptions"
                or "WithCredentials" or "WithWaitForReady")
            return ResolveCallOptions(receiver, context, cancellationToken, depth + 1);

        if (operation is not IObjectCreationOperation { Initializer: null } creation) return ProtectionState.Unknown;

        var explicitDeadline = creation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "deadline");
        var state = explicitDeadline is null
            ? ProtectionState.Unprotected
            : ResolveDateTime(explicitDeadline.Value, context, cancellationToken);
        if (state is ProtectionState.Protected) return state;

        var contextPropagationToken = creation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "propagationToken");
        return contextPropagationToken is not null && contextPropagationToken.Value.ConstantValue is not
            { HasValue: true, Value: null }
            ? ProtectionState.Unknown
            : state;
    }

    private bool HasKnownAbsentContextPropagationToken(IOperation operation, CancellationToken cancellationToken)
    {
        operation = _locals.Resolve(operation, cancellationToken);
        var depth = 0;
        while (operation is IInvocationOperation
               {
                   TargetMethod.Name: "WithDeadline" or "WithCancellationToken" or "WithHeaders" or "WithWriteOptions"
                   or "WithCredentials" or "WithWaitForReady",
                   Instance: { } instance
               } invocation
               && GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.CallOptions))
        {
            if (++depth > 32) return false;
            operation = _locals.Resolve(instance, cancellationToken);
        }

        if (operation is IDefaultValueOperation) return true;

        if (operation is not IObjectCreationOperation { Initializer: null } creation) return false;

        var contextPropagationToken = creation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "propagationToken");
        return contextPropagationToken is null || contextPropagationToken.Value.ConstantValue is
            { HasValue: true, Value: null };
    }

    private ProtectionState ResolveDateTime(IOperation operation,
        IParameterSymbol? context,
        CancellationToken cancellationToken)
    {
        operation = _locals.Resolve(operation, cancellationToken);
        if (context is not null
            && new ParentDeadlineResolver(symbols, _locals).IsInherited(operation, context, cancellationToken))
            return ProtectionState.Protected;
        if (operation.ConstantValue is { HasValue: true, Value: null }
            || (operation is IDefaultValueOperation && GrpcSymbols.IsNullableDateTime(operation.Type)) ||
            operation is IFieldReferenceOperation
            {
                Field: { Name: "MaxValue", ContainingType.SpecialType: SpecialType.System_DateTime }
            })
            return ProtectionState.Unprotected;

        return operation switch
        {
            IFieldReferenceOperation
            {
                Field: { Name: "MinValue", ContainingType.SpecialType: SpecialType.System_DateTime }
            } or IDefaultValueOperation { Type.SpecialType: SpecialType.System_DateTime } => ProtectionState
                .Protected,
            IObjectCreationOperation { Type.SpecialType: SpecialType.System_DateTime, Initializer: null } creation =>
                ResolveDateTimeConstructor(creation),
            _ => ProtectionState.Unknown
        };
    }

    private static ProtectionState ResolveDateTimeConstructor(IObjectCreationOperation creation)
    {
        if (creation.Arguments.Any(argument => !argument.Value.ConstantValue.HasValue
                                               || argument.Parameter?.Name is not ("ticks" or "kind" or "year"
                                                   or "month" or "day" or "hour" or "minute" or "second"
                                                   or "millisecond")))
            return ProtectionState.Unknown;

        object? Value(string name)
        {
            return creation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == name)
                ?.Value.ConstantValue.Value;
        }

        try
        {
            var kind = (DateTimeKind)(int)(Value("kind") ?? (int)DateTimeKind.Unspecified);
            var value = Value("ticks") is long ticks
                ? new DateTime(ticks, kind)
                : creation.Arguments.IsEmpty
                    ? default
                    : new DateTime(
                        (int)Value("year")!,
                        (int)Value("month")!,
                        (int)Value("day")!,
                        (int)(Value("hour") ?? 0),
                        (int)(Value("minute") ?? 0),
                        (int)(Value("second") ?? 0),
                        (int)(Value("millisecond") ?? 0),
                        kind);

            return value == DateTime.MaxValue ? ProtectionState.Unprotected : ProtectionState.Protected;
        }
        catch (ArgumentException)
        {
            return ProtectionState.Unknown;
        }
    }
}

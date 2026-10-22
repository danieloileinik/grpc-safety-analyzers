using System;
using System.Collections.Immutable;
using System.Linq;
using GrpcSafety.Analyzers.Analysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace GrpcSafety.Analyzers.Semantics;

internal sealed class DeadlineResolver(GrpcSymbols symbols)
{
    public ProtectionState Resolve(GrpcCall call)
    {
        ImmutableArray<IArgumentOperation> arguments = call.Invocation.Arguments;
        IArgumentOperation? deadline = arguments.FirstOrDefault(argument =>
            argument.Parameter is { Name: "deadline" } parameter && GrpcSymbols.IsNullableDateTime(parameter.Type));
        if (deadline is not null) return ResolveDateTime(deadline.Value);

        IArgumentOperation? options =
            arguments.FirstOrDefault(argument => GrpcSymbols.Is(argument.Parameter?.Type, symbols.CallOptions));
        return options is null ? ProtectionState.Unknown : ResolveCallOptions(options.Value);
    }

    internal static IOperation? Unwrap(IOperation? operation)
    {
        while (operation is IConversionOperation { OperatorMethod: null } conversion) operation = conversion.Operand;

        return operation;
    }

    private ProtectionState ResolveCallOptions(IOperation operation)
    {
        operation = Unwrap(operation)!;
        if (operation is IDefaultValueOperation) return ProtectionState.Unprotected;

        if (operation is IInvocationOperation { TargetMethod.Name: "WithDeadline" } invocation
            && GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.CallOptions)
            && invocation.Arguments.Length is 1)
        {
            ProtectionState deadline = ResolveDateTime(invocation.Arguments[0].Value);
            if (deadline is ProtectionState.Protected) return deadline;

            // An opaque CallOptions instance may carry a propagation token.
            return deadline is ProtectionState.Unprotected
                   && invocation.Instance is not null
                   && HasKnownAbsentPropagationToken(invocation.Instance)
                ? ProtectionState.Unprotected
                : ProtectionState.Unknown;
        }

        if (operation is not IObjectCreationOperation { Initializer: null } creation) return ProtectionState.Unknown;

        IArgumentOperation? explicitDeadline = creation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "deadline");
        ProtectionState state = explicitDeadline is null ? ProtectionState.Unprotected : ResolveDateTime(explicitDeadline.Value);
        if (state is ProtectionState.Protected) return state;

        IArgumentOperation? propagation = creation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "propagationToken");
        return propagation is not null && propagation.Value.ConstantValue is not { HasValue: true, Value: null }
            ? ProtectionState.Unknown
            : state;
    }

    private bool HasKnownAbsentPropagationToken(IOperation operation)
    {
        operation = Unwrap(operation)!;
        while (operation is IInvocationOperation
               {
                   TargetMethod.Name: "WithDeadline", Arguments.Length: 1, Instance: { } instance
               } invocation
               && GrpcSymbols.Is(invocation.TargetMethod.ContainingType, symbols.CallOptions))
            operation = Unwrap(instance)!;

        if (operation is IDefaultValueOperation) return true;

        if (operation is not IObjectCreationOperation { Initializer: null } creation) return false;

        IArgumentOperation? propagation = creation.Arguments.FirstOrDefault(argument =>
            argument.Parameter?.Name is "propagationToken");
        return propagation is null || propagation.Value.ConstantValue is { HasValue: true, Value: null };
    }

    private static ProtectionState ResolveDateTime(IOperation operation)
    {
        operation = Unwrap(operation)!;
        if (operation.ConstantValue is { HasValue: true, Value: null }
            || (operation is IDefaultValueOperation && GrpcSymbols.IsNullableDateTime(operation.Type)))
            return ProtectionState.Unprotected;

        if (operation is IFieldReferenceOperation
            {
                Field: { Name: "MaxValue", ContainingType.SpecialType: SpecialType.System_DateTime }
            }) return ProtectionState.Unprotected;

        if (operation is IFieldReferenceOperation
            {
                Field: { Name: "MinValue", ContainingType.SpecialType: SpecialType.System_DateTime }
            }
            or IDefaultValueOperation { Type.SpecialType: SpecialType.System_DateTime })
            return ProtectionState.Protected;

        if (operation is IObjectCreationOperation
            {
                Type.SpecialType: SpecialType.System_DateTime, Initializer: null
            } creation) return ResolveDateTimeConstructor(creation);

        return ProtectionState.Unknown;
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
            DateTime value = Value("ticks") is long ticks
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

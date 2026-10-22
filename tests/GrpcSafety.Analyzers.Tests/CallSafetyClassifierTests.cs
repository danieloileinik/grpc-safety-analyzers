using System;
using GrpcSafety.Analyzers.Analysis;
using GrpcSafety.Analyzers.Configuration;
using Xunit;

namespace GrpcSafety.Analyzers.Tests;

public sealed class CallSafetyClassifierTests
{
    [Theory]
    [InlineData("Unknown", "Unknown", false, false)]
    [InlineData("Unprotected", "Unknown", false, false)]
    [InlineData("Unknown", "Unprotected", true, false)]
    [InlineData("Unknown", "Unknown", false, true)]
    [InlineData("Unprotected", "Unknown", true, true)]
    [InlineData("Protected", "Unknown", false, false)]
    [InlineData("Protected", "Unknown", false, true)]
    [InlineData("Unknown", "Protected", true, false)]
    [InlineData("Unprotected", "Protected", true, false)]
    public void UnknownOrProtectedEvidenceDoesNotReport(
        string explicitDeadline,
        string centralDeadline,
        bool registrationResolved,
        bool strict)
    {
        // Arrange
        var deadline = Enum.Parse<ProtectionState>(explicitDeadline);
        var configuration = new ClientConfiguration(registrationResolved, Enum.Parse<ProtectionState>(centralDeadline));

        // Act
        CallSafetyClassification classification = CallSafetyClassifier.Classify(
            deadline,
            configuration,
            ProtectionState.Unknown,
            RetrySafety.Unknown,
            new GrpcSafetyOptions(strict));

        // Assert
        Assert.Null(classification.Diagnostic);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedAbsenceReportsMissingDeadline(bool strict)
    {
        // Arrange
        var configuration = ClientConfiguration.WithoutCentralDeadline;

        // Act
        CallSafetyClassification classification = CallSafetyClassifier.Classify(
            ProtectionState.Unprotected,
            configuration,
            ProtectionState.Unknown,
            RetrySafety.Unknown,
            new GrpcSafetyOptions(strict));

        // Assert
        Assert.Equal(ProtectionState.Unprotected, classification.Deadline);
        Assert.Equal(DiagnosticIds.MissingDeadline, classification.Diagnostic?.Id);
    }

    [Fact]
    public void DefaultEvidenceIsUnknown()
    {
        // Arrange
        var configuration = default(ClientConfiguration);
        var retry = default(RetrySafety);

        // Act
        CallSafetyClassification classification = CallSafetyClassifier.Classify(default, configuration, default, retry, default);

        // Assert
        Assert.Equal(ProtectionState.Unknown, classification.Deadline);
        Assert.Equal(ProtectionState.Unknown, classification.Propagation);
        Assert.Equal(ProtectionState.Unknown, classification.Retry);
        Assert.Null(classification.Diagnostic);
    }
}

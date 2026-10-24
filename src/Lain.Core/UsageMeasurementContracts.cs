namespace Lain.Core;

/// <summary>KnownReported is null when Samples is zero; it never substitutes for a full total.</summary>
public sealed record TokenComponentMeasurementDto(long? KnownReported, long Samples, long Eligible);

public sealed record TokenMeasurementsDto(TokenComponentMeasurementDto Input, TokenComponentMeasurementDto Output,
    TokenComponentMeasurementDto Cached, TokenComponentMeasurementDto CacheCreation,
    TokenComponentMeasurementDto Reasoning, TokenComponentMeasurementDto Total);

/// <summary>Allowlisted machine categories/actions only; no upstream body or exception text.</summary>
public sealed record AttemptErrorDto(string Category, string Evidence, string Action);

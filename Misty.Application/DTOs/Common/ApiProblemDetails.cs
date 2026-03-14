namespace Misty.Application.DTOs.Common;

/// RFC 7807-compliant error response. Mirrors the shape of ASP.NET Core's ProblemDetails and ValidationProblemDetails without depending on Microsoft.AspNetCore.Mvc.
public record ApiProblemDetails
{
    /// A URI reference that identifies the problem type.
    public string Type { get; init; } = "about:blank";

    /// A short, human-readable summary of the problem type.
    public required string Title { get; init; }

    /// The HTTP status code.
    public int Status { get; init; }

    /// A human-readable explanation specific to this occurrence of the problem.
    public string? Detail { get; init; }

    /// A URI reference that identifies the specific occurrence of the problem.
    public string? Instance { get; init; }

    /// Validation errors keyed by field name (mirrors ValidationProblemDetails.Errors). Only included for validation errors (400 Bad Request).
    public IDictionary<string, string[]>? Errors { get; init; }
}

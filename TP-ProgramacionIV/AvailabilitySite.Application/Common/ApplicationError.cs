namespace AvailabilitySite.Application.Common;

public enum ApplicationErrorType
{
    Validation,
    NotFound,
    Unauthenticated,
    Forbidden,
    DependencyUnavailable,
    OutcomeUnknown
}

public sealed record ApplicationError(
    ApplicationErrorType Type,
    string Code,
    string Message);

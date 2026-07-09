namespace ProductionLinePlanner.Application.Common;

public sealed record Error
{
    public Error(string code, string message, IReadOnlyList<ValidationFailure>? details = null)
    {
        Code = code;
        Message = message;
        Details = details ?? [];
    }

    public string Code { get; }

    public string Message { get; }

    public IReadOnlyList<ValidationFailure> Details { get; }

    public static readonly Error None = new(string.Empty, string.Empty);

    public bool IsNone => string.IsNullOrWhiteSpace(Code);
}

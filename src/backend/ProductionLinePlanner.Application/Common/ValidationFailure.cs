namespace ProductionLinePlanner.Application.Common;

public sealed record ValidationFailure
{
    public ValidationFailure(string field, string message, string? code = null)
    {
        Field = field;
        Message = message;
        Code = code;
    }

    public string Field { get; }

    public string Message { get; }

    public string? Code { get; }
}

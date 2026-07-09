using Microsoft.AspNetCore.Http;

public static class ApiResponse
{
    public static IResult Failure(string code, string message, int statusCode = 400)
    {
        return Results.Json(
            new { success = false, error = new { code, message } },
            statusCode: statusCode);
    }

    public static object Success(object? data, string message = "OK") => new { success = true, message, data };
}

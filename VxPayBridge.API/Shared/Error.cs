namespace VxPayBridge.API.Shared;

public class Error
{
    public string Code { get; }
    public string Message { get; }

    public Error(string code, string message)
    {
        Code = code;
        Message = message;
    }

    public static Error None => new(string.Empty, string.Empty);
    public static Error NullValue => new("Error.NullValue", "The specified result value is null.");
    public static Error BadRequest(string message) => new("400", message);
    public static Error Unauthorized(string message = "Unauthorized") => new("401", message);
    public static Error NotFound(string message = "Not Found") => new("404", message);
}

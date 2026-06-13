namespace BoardVerse.API.Authentication
{
    internal static class JwtAuthFailureContext
    {
        public const string StatusCodeItemKey = "AuthFailureStatusCode";
        public const string MessageItemKey = "AuthFailureMessage";
        public const string ResponseWrittenItemKey = "AuthJsonResponseWritten";

        public static void Set(HttpContext httpContext, int statusCode, string message)
        {
            httpContext.Items[StatusCodeItemKey] = statusCode;
            httpContext.Items[MessageItemKey] = message;
        }

        public static int? GetStatusCode(HttpContext httpContext) =>
            httpContext.Items.TryGetValue(StatusCodeItemKey, out var value) && value is int code
                ? code
                : null;

        public static string? GetMessage(HttpContext httpContext) =>
            httpContext.Items.TryGetValue(MessageItemKey, out var value) ? value as string : null;

        public static void MarkResponseWritten(HttpContext httpContext) =>
            httpContext.Items[ResponseWrittenItemKey] = true;

        public static bool IsResponseWritten(HttpContext httpContext) =>
            httpContext.Items.ContainsKey(ResponseWrittenItemKey);
    }
}

using System;
using Microsoft.SemanticKernel;

namespace ABCCons.Function.Exceptions
{
    public class ContentFilterException : Exception
    {
        public ContentFilterException(string message) : base(message)
        {
        }

        public ContentFilterException(string message, Exception innerException) : base(message, innerException)
        {
        }

        public static bool IsContentFilterException(Exception? ex)
        {
            if (ex == null) return false;

            if (ex is ContentFilterException) return true;

            var message = ex.Message ?? string.Empty;
            if (message.Contains("content_filter", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("content management policy", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("triggered Azure OpenAI's content management policy", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (ex is HttpOperationException httpEx)
            {
                if (httpEx.ResponseContent != null && 
                    (httpEx.ResponseContent.Contains("content_filter", StringComparison.OrdinalIgnoreCase) ||
                     httpEx.ResponseContent.Contains("content management policy", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            if (ex is AggregateException aggEx)
            {
                foreach (var inner in aggEx.InnerExceptions)
                {
                    if (IsContentFilterException(inner)) return true;
                }
            }

            if (ex.InnerException != null)
            {
                return IsContentFilterException(ex.InnerException);
            }

            return false;
        }
    }
}

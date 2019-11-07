using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace NetCore3Logging.Middlewares
{
    public class LoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<LoggingMiddleware> _logger;

        public LoggingMiddleware(
            RequestDelegate next,
            ILogger<LoggingMiddleware> logger
            )
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext httpContext)
        {
            try
            {
                httpContext.Request.EnableBuffering();

                var request = httpContext.Request;
                var stopWatch = Stopwatch.StartNew();
                var requestTime = DateTime.UtcNow;
                var requestBodyContent = await ReadRequestBodyAsync(request).ConfigureAwait(false);
                var originalBodyStream = httpContext.Response.Body;
                await using var responseBody = new MemoryStream();
                var response = httpContext.Response;
                response.Body = responseBody;
                await _next(httpContext).ConfigureAwait(false);
                stopWatch.Stop();

                var responseBodyContent = await ReadResponseBodyAsync(response).ConfigureAwait(false);
                await responseBody.CopyToAsync(originalBodyStream).ConfigureAwait(false);

                SafeLog(requestTime,
                    stopWatch.ElapsedMilliseconds,
                    response.StatusCode,
                    request.Method,
                    request.Path,
                    request.QueryString.ToString(),
                    requestBodyContent,
                    responseBodyContent);
            }
            catch (Exception ex)
            {
                await _next(httpContext).ConfigureAwait(false);
            }
        }

        private static async Task<string> ReadRequestBodyAsync(HttpRequest request)
        {
            var buffer = new byte[Convert.ToInt32(request.ContentLength)];
            await request.Body.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
            var bodyAsText = Encoding.UTF8.GetString(buffer);
            request.Body.Seek(0, SeekOrigin.Begin);

            return bodyAsText;
        }

        private static async Task<string> ReadResponseBodyAsync(HttpResponse response)
        {
            response.Body.Seek(0, SeekOrigin.Begin);
            using var streamReader = new StreamReader(response.Body);
            var bodyAsText = await streamReader.ReadToEndAsync().ConfigureAwait(false);
            response.Body.Seek(0, SeekOrigin.Begin);

            return bodyAsText;
        }

        private void SafeLog(DateTime requestTime,
            long elapsedTimeInMs,
            int statusCode,
            string method,
            string path,
            string queryString,
            string requestBody,
            string responseBody)
        {

            if (requestBody.Length > 300)
            {
                requestBody = $"(Truncated to 300 chars) {requestBody.Substring(0, 300)}";
            }

            if (responseBody.Length > 100)
            {
                responseBody = $"(Truncated to 100 chars) {responseBody.Substring(0, 100)}";
            }

            if (queryString.Length > 100)
            {
                queryString = $"(Truncated to 100 chars) {queryString.Substring(0, 100)}";
            }

            var log = new
            {
                RequestTime = requestTime,
                ElapsedTimeInMs = elapsedTimeInMs,
                StatusCode = statusCode,
                Method = method,
                Path = path,
                QueryString = queryString,
                RequestBody = requestBody,
                ResponseBody = responseBody
            };

            _ = Task.Run(async () =>
            {
                _logger.LogInformation("Incoming HTTP request info: {@callInfo}", log);
            });
        }
    }
}

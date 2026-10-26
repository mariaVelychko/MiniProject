using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using ScheduleService.Models;
using ScheduleService.Repositories;

namespace ScheduleService.Infrastructure;

/// <summary>
/// Ідемпотентність створення: запит із заголовком Idempotency-Key виконується один раз, повтор повертає
/// збережену відповідь. Той самий ключ з іншим тілом — 422. Зберігаються лише успішні (2xx) відповіді,
/// тож після помилки клієнт може повторити запит із тим самим ключем. Без заголовка поведінка не змінюється.
/// </summary>
public sealed class IdempotencyFilter(
    IIdempotencyStore store,
    IUrlHelperFactory urls,
    TimeProvider clock) : IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayHeader = "Idempotency-Replayed";
    private const int MaxKeyLength = 128;

    // Захист від паралельних запитів з одним ключем (в межах одного процесу).
    private static readonly SemaphoreSlim[] Gates =
        Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var key = http.Request.Headers[HeaderName].ToString().Trim();

        if (key.Length == 0)
        {
            await next();
            return;
        }

        if (key.Length > MaxKeyLength)
        {
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Некоректний Idempotency-Key",
                Detail = $"Довжина ключа не може перевищувати {MaxKeyLength} символів.",
            })
            { StatusCode = StatusCodes.Status400BadRequest };
            return;
        }

        var ct = http.RequestAborted;
        var fingerprint = Fingerprint(context);
        var gate = Gates[(int)((uint)StringComparer.Ordinal.GetHashCode(key) % Gates.Length)];

        await gate.WaitAsync(ct);

        try
        {
            var existing = await store.FindAsync(key, ct);

            if (existing is not null)
            {
                context.Result = existing.Fingerprint == fingerprint
                    ? new StoredResponseResult(existing)
                    : new ObjectResult(new ProblemDetails
                    {
                        Status = StatusCodes.Status422UnprocessableEntity,
                        Title = "Idempotency-Key уже використано з іншим запитом",
                        Detail = "Використайте новий ключ для іншого запиту.",
                    })
                    { StatusCode = StatusCodes.Status422UnprocessableEntity };
                return;
            }

            var executed = await next();

            if (executed.Exception is null && executed.Result is ObjectResult { StatusCode: >= 200 and < 300 } ok)
            {
                string? location = executed.Result is CreatedAtActionResult created
                    ? urls.GetUrlHelper(context).Action(created.ActionName, created.ControllerName, created.RouteValues)
                    : null;

                await store.SaveAsync(new IdempotencyRecord
                {
                    Key = key,
                    Fingerprint = fingerprint,
                    StatusCode = ok.StatusCode!.Value,
                    ResponseBody = ok.Value is null ? null : JsonSerializer.Serialize(ok.Value, ok.Value.GetType(), Json),
                    Location = location,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                }, ct);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static string Fingerprint(ActionExecutingContext context)
    {
        var args = context.ActionArguments
            .Where(a => a.Value is not CancellationToken && a.Key != "idempotencyKey")
            .OrderBy(a => a.Key, StringComparer.Ordinal)
            .ToDictionary(a => a.Key, a => a.Value);

        var http = context.HttpContext.Request;
        var raw = $"{http.Method}|{http.Path}|{JsonSerializer.Serialize(args, Json)}";

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    private sealed class StoredResponseResult(IdempotencyRecord record) : IActionResult
    {
        public async Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = record.StatusCode;
            response.Headers[ReplayHeader] = "true";

            if (record.Location is not null)
            {
                response.Headers.Location = record.Location;
            }

            if (record.ResponseBody is not null)
            {
                response.ContentType = "application/json; charset=utf-8";
                await response.WriteAsync(record.ResponseBody, context.HttpContext.RequestAborted);
            }
        }
    }
}

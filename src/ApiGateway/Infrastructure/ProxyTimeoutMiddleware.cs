using Microsoft.Extensions.Options;

namespace ApiGateway.Infrastructure;

/// <summary>
/// Таймаут на кожен проксійований виклик (ЛР3, 3.2). Без нього один завислий сервіс тримав би запити шлюзу
/// (за замовчуванням Ocelot чекає до 90 с), потоки накопичувалися б — і разом із сервісом лягав би весь шлюз.
///
/// Спосіб: на час проксіювання підміняємо HttpContext.RequestAborted на токен із дедлайном; Ocelot передає його в
/// HttpClient. Далі відповіді-«порожнечі», які згенерував сам шлюз, перетворюємо на ProblemDetails:
/// дедлайн → 504, сервіс не відповідає/обірвав з'єднання → 503. Відповіді самих сервісів (мають тіло) не чіпаємо.
/// </summary>
public sealed class ProxyTimeoutMiddleware(RequestDelegate next, IOptions<GatewayOptions> options, ILogger<ProxyTimeoutMiddleware> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var original = ctx.RequestAborted;
        var timeout = TimeSpan.FromSeconds(options.Value.Timeouts.ProxySeconds);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(original);
        deadline.CancelAfter(timeout);
        ctx.RequestAborted = deadline.Token;

        // Відповідь буферизується: поки Ocelot не закінчив, жодного байта клієнту не пішло, тож помилку шлюзу
        // (порожню відповідь без тіла) ще можна замінити на ProblemDetails. Відповіді шлюзу малі (JSON), тож це прийнятно.
        var originalBody = ctx.Response.Body;
        await using var buffer = new MemoryStream();
        ctx.Response.Body = buffer;

        var threw = false;

        try
        {
            await next(ctx);
        }
        catch (OperationCanceledException) when (!original.IsCancellationRequested && deadline.IsCancellationRequested)
        {
            threw = true;
        }
        finally
        {
            ctx.RequestAborted = original;
            ctx.Response.Body = originalBody;
        }

        if (original.IsCancellationRequested || ctx.Response.HasStarted)
        {
            return;
        }

        if (threw || deadline.IsCancellationRequested)
        {
            log.LogWarning("Сервіс не відповів за {Seconds} с: {Method} {Path}", timeout.TotalSeconds, ctx.Request.Method, ctx.Request.Path);

            await Problems.WriteAsync(ctx, StatusCodes.Status504GatewayTimeout, "Сервіс не відповів вчасно",
                $"Сервіс не відповів за {timeout.TotalSeconds:0.#} с. Спробуйте пізніше.", ct: original);
            return;
        }

        if (ctx.Response.StatusCode >= StatusCodes.Status500InternalServerError && buffer.Length == 0)
        {
            // Тіла немає: помилку створив сам Ocelot (сервіс недоступний, з'єднання обірвано).
            log.LogWarning("Сервіс недоступний: {Method} {Path} (статус {Status})", ctx.Request.Method, ctx.Request.Path, ctx.Response.StatusCode);

            await Problems.WriteAsync(ctx, StatusCodes.Status503ServiceUnavailable, "Сервіс недоступний",
                "Сервіс, якому адресовано запит, зараз недоступний. Спробуйте пізніше.", ct: original);
            return;
        }

        buffer.Position = 0;

        if (buffer.Length > 0)
        {
            await buffer.CopyToAsync(originalBody, original);
        }
    }
}

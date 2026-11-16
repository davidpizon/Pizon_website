using Pizon.Web.Data;

namespace Pizon.Web.Auth;

/// <summary>
/// Rejects state-changing calls without the <c>X-PIZON-BBS: 1</c> header. Browsers can't add a custom header
/// cross-site without a CORS preflight, and CORS is not enabled, so this blocks cross-site request forgery.
/// </summary>
public sealed class CsrfHeaderFilter : IEndpointFilter
{
    /// <summary>The header name.</summary>
    public const string HeaderName = "X-PIZON-BBS";

    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var request = context.HttpContext.Request;
        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return await next(context);
        }

        return request.Headers[HeaderName] == "1" ? await next(context) : Results.BadRequest("missing " + HeaderName + " header");
    }
}

/// <summary>
/// Requires a signed-in caller with a handle who is not banned. The user is stored for the handler
/// (<see cref="CurrentUser"/>).
/// </summary>
public sealed class MemberFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var callers = http.RequestServices.GetRequiredService<CallerDirectory>();
        var user = await callers.GetAsync(http.User, http.RequestAborted);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (user.Handle is null || user.Banned)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: user.Banned ? "banned" : "choose a handle first");
        }

        http.Items[CurrentUser.ItemKey] = user;
        return await next(context);
    }
}

/// <summary>
/// Requires the SysOp. Everyone else gets 404, so the console doesn't advertise itself.
/// </summary>
public sealed class SysopFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var callers = http.RequestServices.GetRequiredService<CallerDirectory>();
        var user = await callers.GetAsync(http.User, http.RequestAborted);
        if (user?.Handle is null || !callers.IsSysop(user))
        {
            return Results.NotFound();
        }

        http.Items[CurrentUser.ItemKey] = user;
        return await next(context);
    }
}

/// <summary>
/// Reads the user stored by <see cref="MemberFilter"/> or <see cref="SysopFilter"/>.
/// </summary>
public static class CurrentUser
{
    /// <summary>The <see cref="HttpContext.Items"/> key.</summary>
    public const string ItemKey = "pizon.user";

    /// <summary>
    /// Gets the filtered user.
    /// </summary>
    /// <param name="http">The request.</param>
    /// <returns>The user.</returns>
    /// <exception cref="InvalidOperationException">The endpoint has no member or SysOp filter.</exception>
    public static BbsUser Get(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.Items[ItemKey] as BbsUser ?? throw new InvalidOperationException("Endpoint is missing MemberFilter or SysopFilter.");
    }
}

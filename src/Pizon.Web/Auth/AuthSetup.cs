using System.Security.Claims;
using System.Text.Json;
using AspNet.Security.OAuth.Discord;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OAuth;
using Pizon.Web.Client.Api;
using Pizon.Web.Data;
using Pizon.Web.Services;

namespace Pizon.Web.Auth;

/// <summary>
/// Sign-in wiring: the cookie, Discord OAuth2 (scope <c>identify</c> only), the Development sign-in, and the
/// identity and handle endpoints.
/// </summary>
public static class AuthSetup
{
    /// <summary>The sign-in cookie name.</summary>
    public const string CookieName = ".pizon.bbs";

    /// <summary>Where a failed or cancelled Discord sign-in returns to.</summary>
    public const string LoginFailedUrl = "/login?error=failed";

    /// <summary>
    /// Checks whether Discord sign-in is configured.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns><see langword="true"/> when a client id and secret are set.</returns>
    public static bool DiscordConfigured(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return !string.IsNullOrWhiteSpace(configuration["Discord:ClientId"]) && !string.IsNullOrWhiteSpace(configuration["Discord:ClientSecret"]);
    }

    /// <summary>
    /// Makes a return URL safe: only local paths are allowed.
    /// </summary>
    /// <param name="returnUrl">The requested return URL.</param>
    /// <returns>The URL when it is a local path, otherwise <c>/</c>.</returns>
    public static string SafeReturnUrl(string? returnUrl) =>
        returnUrl is { Length: > 0 } url && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";

    /// <summary>
    /// Reads the best display name from a Discord user object: <c>global_name</c>, then <c>username</c>.
    /// </summary>
    /// <param name="user">The Discord <c>/users/@me</c> JSON.</param>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public static string? DiscordName(JsonElement user)
    {
        foreach (var property in new[] { "global_name", "username" })
        {
            if (user.ValueKind == JsonValueKind.Object && user.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()))
            {
                return value.GetString();
            }
        }

        return null;
    }

    /// <summary>
    /// Adds the cookie, Discord (when configured) and the caller services.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    public static void AddBbsAuth(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.Configure<SysopOptions>(builder.Configuration.GetSection("SysOp"));
        builder.Services.AddScoped<CallerDirectory>();
        var auth = builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = CookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.ExpireTimeSpan = TimeSpan.FromDays(30);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            });

        if (DiscordConfigured(builder.Configuration))
        {
            auth.AddDiscord(options =>
            {
                options.ClientId = builder.Configuration["Discord:ClientId"]!;
                options.ClientSecret = builder.Configuration["Discord:ClientSecret"]!;
                options.Scope.Clear();
                options.Scope.Add("identify");
                options.SaveTokens = false;
                options.Events = new OAuthEvents
                {
                    OnCreatingTicket = OnDiscordTicketAsync,
                    OnRemoteFailure = context =>
                    {
                        context.Response.Redirect(LoginFailedUrl);
                        context.HandleResponse();
                        return Task.CompletedTask;
                    },
                };
            });
        }

        builder.Services.AddAuthorization();
    }

    /// <summary>
    /// Maps <c>/auth/*</c> and <c>/api/me</c>.
    /// </summary>
    /// <param name="app">The application.</param>
    public static void MapBbsAuth(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var discord = DiscordConfigured(app.Configuration);

        app.MapGet("/auth/discord", (string? returnUrl) => discord
            ? Results.Challenge(new AuthenticationProperties { RedirectUri = SafeReturnUrl(returnUrl) }, [DiscordAuthenticationDefaults.AuthenticationScheme])
            : Results.Redirect(LoginFailedUrl));

        app.MapPost("/auth/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok();
        }).AddEndpointFilter<CsrfHeaderFilter>();

        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/auth/dev", DevSignInAsync);
        }

        app.MapGet("/api/me", async (HttpContext http, CallerDirectory callers) =>
        {
            var user = await callers.GetAsync(http.User, http.RequestAborted);
            var dev = app.Environment.IsDevelopment();
            return user is null
                ? new MeResponse(false, null, null, SecurityLevels.Guest, false, discord, dev)
                : new MeResponse(true, user.Handle, Limits.SuggestHandle(user.DiscordName), callers.LevelOf(user), callers.IsSysop(user) && user.Handle is not null, discord, dev);
        });

        app.MapPost("/api/me/handle", SetHandleAsync).AddEndpointFilter<CsrfHeaderFilter>().RequireRateLimiting(RateLimits.Writes);
    }

    /// <summary>
    /// Chooses or changes the signed-in caller's handle.
    /// </summary>
    /// <param name="request">The requested handle.</param>
    /// <param name="http">The request.</param>
    /// <param name="callers">The caller directory.</param>
    /// <param name="db">The database.</param>
    /// <param name="filter">The word filter.</param>
    /// <returns>200, 400 with a reason, 401 for guests or 403 for banned users.</returns>
    internal static async Task<IResult> SetHandleAsync(SetHandleRequest request, HttpContext http, CallerDirectory callers, BbsDbContext db, WordFilter filter)
    {
        var user = await callers.GetAsync(http.User, http.RequestAborted);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        if (user.Banned)
        {
            return Results.Problem(statusCode: StatusCodes.Status403Forbidden, detail: "banned");
        }

        var handle = request?.Handle?.Trim();
        if (!Limits.IsValidHandleShape(handle))
        {
            return Results.BadRequest("handles are 3-16 letters, digits, _ or -");
        }

        if (!filter.IsClean(handle!.Replace('-', ' ')) || handle.Equals("GUEST", StringComparison.OrdinalIgnoreCase) || handle.Equals("SYSOP", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest("that handle is not allowed");
        }

        var key = handle.ToUpperInvariant();
        if (db.Users.Any(u => u.HandleKey == key && u.Id != user.Id))
        {
            return Results.Conflict("that handle is taken");
        }

        user.Handle = handle;
        user.HandleKey = key;
        await db.SaveChangesAsync(http.RequestAborted);
        return Results.Ok();
    }

    /// <summary>
    /// Development only: signs in a fake user (<c>tester</c> or <c>sysop</c>) without Discord.
    /// </summary>
    /// <param name="user">Which fake user: <c>tester</c> or <c>sysop</c>.</param>
    /// <param name="returnUrl">Where to go afterwards.</param>
    /// <param name="http">The request.</param>
    /// <param name="callers">The caller directory.</param>
    /// <returns>A redirect.</returns>
    internal static async Task<IResult> DevSignInAsync(string? user, string? returnUrl, HttpContext http, CallerDirectory callers)
    {
        var name = user is "sysop" ? "sysop" : "tester";
        var record = await callers.SignedInAsync(BbsClaims.DevPrefix + name, name == "sysop" ? "Dev SysOp" : "Dev Tester", http.RequestAborted);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, BbsClaims.PrincipalFor(record, "dev"));
        return Results.Redirect(SafeReturnUrl(returnUrl));
    }

    private static async Task OnDiscordTicketAsync(OAuthCreatingTicketContext context)
    {
        var discordId = context.Identity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(discordId) || context.Identity is null)
        {
            context.Fail("Discord did not return a user id.");
            return;
        }

        var callers = context.HttpContext.RequestServices.GetRequiredService<CallerDirectory>();
        var user = await callers.SignedInAsync(discordId, DiscordName(context.User), context.HttpContext.RequestAborted);
        context.Principal = BbsClaims.PrincipalFor(user, context.Identity.AuthenticationType ?? "discord");
    }
}

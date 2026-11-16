using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Pizon.Web;
using Pizon.Web.Auth;
using Pizon.Web.Components;
using Pizon.Web.Data;
using Pizon.Web.Endpoints;
using Pizon.Web.Hubs;
using Pizon.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

var connectionString = builder.Configuration.GetConnectionString("bbsdb") ?? "Data Source=App_Data/bbs.db";
var dataDirectory = DataPaths.DirectoryOf(connectionString, builder.Environment.ContentRootPath);
Directory.CreateDirectory(dataDirectory);
builder.Services.AddDbContext<BbsDbContext>(options => options.UseSqlite(connectionString));
builder.Services.AddDataProtection()
    .SetApplicationName("PIZON BBS")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory, "keys")));

builder.Services.AddRazorComponents().AddInteractiveWebAssemblyComponents();
builder.Services.AddSignalR();
builder.Services.AddSingleton<NodeRegistry>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<WordFilterOptions>(builder.Configuration.GetSection("WordFilter"));
builder.Services.AddSingleton<WordFilter>();
builder.Services.Configure<NotifyOptions>(builder.Configuration.GetSection("Notify"));
builder.Services.AddHttpClient<INotifier, WebhookNotifier>(http => http.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<IDoorCatalog, FileDoorCatalog>();
builder.Services.AddBbsRateLimits();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    // Caddy runs in the same Docker network; trust its X-Forwarded-* headers.
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});
builder.AddBbsAuth();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<BbsDbContext>().Database.MigrateAsync();
}

app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

// Friendly 404 pages are for browsers loading pages; API, auth and hub status codes must reach callers unchanged.
app.UseWhen(
    context => HttpMethods.IsGet(context.Request.Method) &&
        !context.Request.Path.StartsWithSegments("/api") &&
        !context.Request.Path.StartsWithSegments("/auth") &&
        !context.Request.Path.StartsWithSegments("/hubs"),
    pages => pages.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true));
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapDefaultEndpoints();
app.MapBbsAuth();
app.MapBoardEndpoints();
app.MapScoreEndpoints();
app.MapSysopEndpoints();
app.MapPlainEndpoint();
app.MapHub<NodeHub>("/" + Pizon.Web.Client.Services.PresenceProtocol.HubPath);
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(Pizon.Web.Client._Imports).Assembly);

await app.RunAsync();

/// <summary>
/// The server entry point (declared so integration tests can reference it).
/// </summary>
public partial class Program
{
}

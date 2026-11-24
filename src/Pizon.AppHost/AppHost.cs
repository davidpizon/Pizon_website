// PIZON BBS local run & test host (.NET Aspire). Not used in production.
//
// Optional settings (AppHost user-secrets or environment):
//   Parameters:discord-client-id / Parameters:discord-client-secret  - enables Discord sign-in
//   Parameters:sysop-discord-id                                      - who gets the SysOp console
//   Parameters:discord-bot-token / Parameters:discord-bulletins-channel - relays a Discord channel as bulletins
//   Pizon:UseNtfy = true                                             - runs a local ntfy container (needs Docker)
//   Pizon:UseSqliteWeb = true                                        - adds the SQLite Web DB viewer (a container)
// Containers need a running Docker or Podman; Aspire refuses to start when the model has a container and
// no healthy runtime, so every container here is opt-in.
// Without them, only the Development sign-in is offered and notifications are logged.
var builder = DistributedApplication.CreateBuilder(args);

var db = builder.AddSqlite("bbsdb");
if (IsOn("Pizon:UseSqliteWeb"))
{
    db.WithSqliteWeb();
}

var web = builder.AddProject<Projects.Pizon_Web>("web")
    .WithReference(db)
    .WaitFor(db)
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/healthz");

if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:discord-client-id"]))
{
    web.WithEnvironment("Discord__ClientId", builder.AddParameter("discord-client-id"))
        .WithEnvironment("Discord__ClientSecret", builder.AddParameter("discord-client-secret", secret: true));
}

if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:sysop-discord-id"]))
{
    web.WithEnvironment("SysOp__DiscordUserId", builder.AddParameter("sysop-discord-id"));
}

if (!string.IsNullOrWhiteSpace(builder.Configuration["Parameters:discord-bot-token"]))
{
    web.WithEnvironment("Discord__BotToken", builder.AddParameter("discord-bot-token", secret: true))
        .WithEnvironment("Discord__Feeds__Bulletins__ChannelId", builder.AddParameter("discord-bulletins-channel"));
}

if (IsOn("Pizon:UseNtfy"))
{
    var ntfy = builder.AddContainer("ntfy", "binwiederhier/ntfy", "v2.11.0")
        .WithArgs("serve")
        .WithHttpEndpoint(targetPort: 80, name: "http");
    web.WithEnvironment("Notify__WebhookUrl", ReferenceExpression.Create($"{ntfy.GetEndpoint("http")}/pizon-sysop"));
}

await builder.Build().RunAsync();

// Reads an on/off switch from configuration.
bool IsOn(string key) => string.Equals(builder.Configuration[key], "true", StringComparison.OrdinalIgnoreCase);

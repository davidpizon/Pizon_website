using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Pizon.Web.Client.Api;
using Pizon.Web.Client.Content;
using Pizon.Web.Client.Doors;
using Pizon.Web.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<IBbsApi, BbsApi>();
builder.Services.AddScoped<ContentStore>();
builder.Services.AddScoped<IPresence, HubPresence>();
builder.Services.AddScoped<IExternalNavigator, ExternalNavigator>();
builder.Services.AddScoped<LazyAssemblyLoader>();
builder.Services.AddScoped<IDoorLoader, LazyDoorLoader>();
builder.Services.AddSingleton(TimeProvider.System);

await builder.Build().RunAsync();

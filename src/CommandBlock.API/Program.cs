using CommandBlock.API;
using CommandBlock.API.Extensions;
using CommandBlock.API.Hubs;
using CommandBlock.API.OpenApi;
using CommandBlock.API.Routing;
using CommandBlock.Infrastructure.Extensions;
using CommandBlock.Infrastructure.Interfaces;
using CommandBlock.Infrastructure.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddCommandBlockConfig(builder.Environment);

builder.Services.AddSpaStaticFiles(options => { options.RootPath = "wwwroot"; });

builder.Services.AddControllers();
builder.Services.AddSignalR();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, HttpCurrentUserService>();

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<OAuth2SecuritySchemeTransformer>();
});

builder.Services.AddMediator(options => { options.ServiceLifetime = ServiceLifetime.Scoped; });

builder.Services.AddCommandBlockDatabase(builder.Configuration);
builder.Services.AddDocker(builder.Configuration);
builder.Services.AddActivityLog();

// World backups to an S3/SeaweedFS bucket.
builder.Services.Configure<CommandBlock.Infrastructure.Options.BackupOptions>(builder.Configuration.GetSection("Backup"));
builder.Services.AddScoped<IBackupStorage, CommandBlock.Infrastructure.Services.S3BackupStorage>();

// Modrinth modpack search (public API, no key). Typed HttpClient with the UA Modrinth asks for.
builder.Services.AddHttpClient<IModrinthClient, CommandBlock.Infrastructure.Services.ModrinthClient>(c =>
{
    c.BaseAddress = new Uri("https://api.modrinth.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd("CommandBlock/1.0 (+https://github.com/PianoNic/CommandBlock)");
    c.Timeout = TimeSpan.FromSeconds(10);
});

// Minecraft version list, proxied from Mojang's official manifest and cached in memory.
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IMinecraftVersionClient, CommandBlock.Infrastructure.Services.MinecraftVersionClient>(c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd("CommandBlock/1.0 (+https://github.com/PianoNic/CommandBlock)");
    c.Timeout = TimeSpan.FromSeconds(10);
});

// Single-host: every Docker operation runs against the local daemon.
builder.Services.AddScoped<IDockerServiceResolver, LocalDockerServiceResolver>();

// Server file browser/editor (Docker copy + exec under each server's /data).
builder.Services.AddScoped<IServerFilesService, CommandBlock.Infrastructure.Services.ServerFilesService>();

// Live server status (state + player counts), shared by the list query and the status stream.
builder.Services.AddScoped<IServerStatusService, CommandBlock.Infrastructure.Services.ServerStatusService>();

// Minecraft hostname router: one public TCP port fronting every provisioned server, routed by the
// address in the client handshake. The resolver is scoped (touches the DbContext); the listener is
// a singleton hosted service that opens a scope per connection.
builder.Services.Configure<RouterOptions>(builder.Configuration.GetSection("Router"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IServerRouteResolver, DbServerRouteResolver>();
builder.Services.AddSingleton<IServerConnectionTracker, ServerConnectionTracker>();
builder.Services.AddHostedService<MinecraftRouter>();
builder.Services.AddHostedService<IdleServerMonitor>();
builder.Services.AddHostedService<CommandBlock.API.BackupSchedulerHostedService>();

// Defaults to no cross-origin allowlist when unset. The desktop build serves the SPA
// same-origin from the sidecar, so it needs none; server deployments set it explicitly.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
// AllowCredentials is required for cross-origin SignalR (the JS client sends withCredentials); it's
// harmless when the SPA is same-origin (the container). Needs explicit origins, which we have.
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var publicAuthority = builder.Configuration["Oidc:Authority"];
        var internalAuthority = builder.Configuration["Oidc:InternalAuthority"] ?? publicAuthority;
        options.MetadataAddress = $"{internalAuthority!.TrimEnd('/')}/.well-known/openid-configuration";
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Oidc:RequireHttpsMetadata", true);
        options.TokenValidationParameters.ValidIssuer = publicAuthority;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.TokenValidationParameters.ValidateAudience = false;

        // Browser WebSockets can't set the Authorization header, so SignalR passes the token as a
        // query-string param on /hubs connections. Pull it in so hub auth works.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) && context.Request.Path.StartsWithSegments("/hubs"))
                    context.Token = accessToken;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();

app.ApplyMigrations();
await app.ApplySeedsAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options =>
    {
        options
            .AddPreferredSecuritySchemes("OAuth2")
            .AddAuthorizationCodeFlow("OAuth2", flow =>
            {
                flow.ClientId = builder.Configuration["Oidc:ClientId"];
                flow.Pkce = Pkce.Sha256;
                flow.SelectedScopes = ["openid", "profile", "email", "roles"];
            });
    }).AllowAnonymous();
}

app.UseStaticFiles();

if (app.Environment.IsProduction())
    app.UseSpaStaticFiles();

app.UseRouting();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ConsoleHub>("/hubs/console").RequireAuthorization();
app.MapHub<ServerStatusHub>("/hubs/status").RequireAuthorization();

if (app.Environment.IsProduction())
    app.MapFallbackToFile("index.html").AllowAnonymous();

app.Run();

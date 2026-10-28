using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.GraphicsGateway.Options;
using Elecciones.GraphicsGateway.Services;
using Elecciones.Infrastructure.Data;
using Elecciones.Infrastructure.Options;
using Elecciones.Infrastructure.Services;
using EleccionesWeb.Components;
using EleccionesWeb.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromHours(12);
    });
builder.Services.AddAuthorization();

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));

builder.Services.Configure<GraphicsEndpointsOptions>(
    builder.Configuration.GetSection(GraphicsEndpointsOptions.SectionName));

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));

builder.Services.Configure<OperationalOptions>(
    builder.Configuration.GetSection(OperationalOptions.SectionName));

var dbOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
if (string.IsNullOrWhiteSpace(dbOptions.Password))
{
    var dbPasswordFromEnv = Environment.GetEnvironmentVariable("ELECCIONES_DB_PASSWORD");
    if (!string.IsNullOrWhiteSpace(dbPasswordFromEnv))
    {
        dbOptions.Password = dbPasswordFromEnv;
    }
}

if (dbOptions.Enabled)
{
    var connectionString =
        $"server={dbOptions.Host};port={dbOptions.Port};uid={dbOptions.User};pwd={dbOptions.Password};database={dbOptions.Name};";

    builder.Services.AddDbContextFactory<EleccionesDbContext>(options =>
    {
        options.UseMySQL(connectionString, mysql =>
        {
            mysql.CommandTimeout(dbOptions.CommandTimeoutSeconds);
        });
    });

    builder.Services.AddSingleton<IEleccionesDataService, MySqlEleccionesDataService>();
}
else
{
    builder.Services.AddSingleton<IEleccionesDataService, InMemoryEleccionesDataService>();
}

builder.Services.AddSingleton<IModuleLockStateStore, JsonFileModuleLockStateStore>();
builder.Services.AddSingleton<IModuleLockService, ModuleLockService>();
builder.Services.AddSingleton<ConfiguredOperatorAuthorizationService>();
builder.Services.AddSingleton<IOperatorAuthorizationService>(sp => sp.GetRequiredService<ConfiguredOperatorAuthorizationService>());
builder.Services.AddSingleton<IOperatorAuthenticationService>(sp => sp.GetRequiredService<ConfiguredOperatorAuthorizationService>());
builder.Services.AddSingleton<ISignalDiagnosticsService, InMemorySignalDiagnosticsService>();
builder.Services.AddSingleton<IOperationStateService, OperationStateService>();
builder.Services.AddSingleton<IGlobalOperationalSettingsService>(_ =>
    new GlobalOperationalSettingsService(new GlobalOperationalSettings
    {
        ElectionSlot = Math.Clamp(dbOptions.DefaultElectionSlot, 1, 3),
        ConnectionType = 0,
        Target = GraphicsTarget.Ipf,
        Oficiales = false,
        Avance = 0,
        ExportName = "BrainStorm",
        IncludeZeroSeats = false,
        AutoRefreshEnabled = false,
        AutoRefreshIntervalSeconds = 5
    }));
builder.Services.AddSingleton<DatabaseChangeListenerService>();
builder.Services.AddHostedService<DatabaseChangeListenerHostedService>();
builder.Services.AddSingleton<ISignalComposer, DefaultSignalComposer>();
builder.Services.AddSingleton<IBrainStormCsvWriter, FileBrainStormCsvWriter>();
builder.Services.AddSingleton<IGraphicsGateway, TcpGraphicsGateway>();
builder.Services.AddSingleton<IOperationalConfigurationService, OperationalConfigurationService>();
builder.Services.AddSingleton<IOperationAuditService, JsonlOperationAuditService>();
builder.Services.AddScoped<IOperationService, OperationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();

app.Use(async (context, next) =>
{
    var authConfig = context.RequestServices.GetRequiredService<ConfiguredOperatorAuthorizationService>();
    if (authConfig.AutoLoginByWorkspace && context.User.Identity?.IsAuthenticated != true)
    {
        var workspace = WorkspaceResolver.Resolve(context.Request.Host.Host, context.Request.Path.Value);
        var operatorId = WorkspaceResolver.GetDefaultOperatorId(workspace);
        var role = WorkspaceResolver.GetDefaultRole(workspace);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, operatorId),
            new(ClaimTypes.Role, role)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        context.User = new ClaimsPrincipal(identity);
    }

    await next();
});

app.UseAuthorization();

app.MapPost("/auth/login", async (
    HttpContext httpContext,
    IOperatorAuthenticationService authService,
    IOperatorAuthorizationService authorizationService) =>
{
    var form = await httpContext.Request.ReadFormAsync();
    var username = form["username"].ToString().Trim();
    var password = form["password"].ToString();
    var returnUrl = form["returnUrl"].ToString();

    if (!authService.ValidateCredentials(username, password))
    {
        return Results.Redirect("/login?error=1");
    }

    var role = authorizationService.GetRole(username);
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, username),
        new(ClaimTypes.Role, role)
    };

    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
    var principal = new ClaimsPrincipal(identity);

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

    var safeReturnUrl = string.IsNullOrWhiteSpace(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
        ? "/"
        : returnUrl;

    return Results.Redirect(safeReturnUrl);
})
.DisableAntiforgery();

app.MapGet("/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

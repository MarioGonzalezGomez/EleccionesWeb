using Elecciones.Application.Abstractions;
using Elecciones.Application.Services;
using Elecciones.GraphicsGateway.Options;
using Elecciones.GraphicsGateway.Services;
using Elecciones.Infrastructure.Data;
using Elecciones.Infrastructure.Options;
using Elecciones.Infrastructure.Services;
using EleccionesWeb.Components;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));

builder.Services.Configure<GraphicsEndpointsOptions>(
    builder.Configuration.GetSection(GraphicsEndpointsOptions.SectionName));

builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));

var dbOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();

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

builder.Services.AddSingleton<IModuleLockService, ModuleLockService>();
builder.Services.AddSingleton<ISignalComposer, DefaultSignalComposer>();
builder.Services.AddSingleton<IBrainStormCsvWriter, FileBrainStormCsvWriter>();
builder.Services.AddSingleton<IGraphicsGateway, TcpGraphicsGateway>();
builder.Services.AddScoped<IOperationService, OperationService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

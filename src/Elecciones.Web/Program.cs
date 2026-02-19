using Elecciones.Application.Abstractions;
using Elecciones.Application.Services;
using Elecciones.GraphicsGateway.Options;
using Elecciones.GraphicsGateway.Services;
using Elecciones.Infrastructure.Options;
using Elecciones.Infrastructure.Services;
using EleccionesWeb.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));

builder.Services.Configure<GraphicsEndpointsOptions>(
    builder.Configuration.GetSection(GraphicsEndpointsOptions.SectionName));

builder.Services.AddSingleton<IModuleLockService, ModuleLockService>();
builder.Services.AddSingleton<IEleccionesDataService, InMemoryEleccionesDataService>();
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

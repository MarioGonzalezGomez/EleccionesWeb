using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Xunit;

namespace Elecciones.Tests;

public sealed class WorkspaceAndGlobalSettingsTests
{
    [Theory]
    [InlineData("faldon.elecciones.local", "/", WorkspaceKind.Faldon)]
    [InlineData("faldon.elecciones.local:5177", "/", WorkspaceKind.Faldon)]
    [InlineData("carton.elecciones.local", "/", WorkspaceKind.Carton)]
    [InlineData("admin.elecciones.local", "/", WorkspaceKind.ControlGeneral)]
    [InlineData("control.elecciones.local", "/", WorkspaceKind.ControlGeneral)]
    [InlineData("localhost:5177", "/faldon", WorkspaceKind.Faldon)]
    [InlineData("localhost:5177", "/operacion/faldon", WorkspaceKind.Faldon)]
    [InlineData("localhost:5177", "/operacion/botonera", WorkspaceKind.Faldon)]
    [InlineData("localhost:5177", "/operacion/pactos", WorkspaceKind.Faldon)]
    [InlineData("localhost:5177", "/carton", WorkspaceKind.Carton)]
    [InlineData("localhost:5177", "/operacion/carton", WorkspaceKind.Carton)]
    [InlineData("localhost:5177", "/admin", WorkspaceKind.ControlGeneral)]
    [InlineData("localhost:5177", "/control", WorkspaceKind.ControlGeneral)]
    [InlineData("localhost:5177", "/operacion/admin", WorkspaceKind.ControlGeneral)]
    [InlineData("localhost:5177", "/operacion/diagnostico", WorkspaceKind.ControlGeneral)]
    public void WorkspaceResolver_ResolvesSubdomainsAndFallbackPaths(string host, string path, WorkspaceKind expected)
    {
        var actual = WorkspaceResolver.Resolve(host, path);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WorkspaceResolver_ReturnsExpectedDefaultOperatorAndRole()
    {
        Assert.Equal("op_faldon", WorkspaceResolver.GetDefaultOperatorId(WorkspaceKind.Faldon));
        Assert.Equal("op_carton", WorkspaceResolver.GetDefaultOperatorId(WorkspaceKind.Carton));
        Assert.Equal("op_admin", WorkspaceResolver.GetDefaultOperatorId(WorkspaceKind.ControlGeneral));

        Assert.Equal("faldon", WorkspaceResolver.GetDefaultRole(WorkspaceKind.Faldon));
        Assert.Equal("carton", WorkspaceResolver.GetDefaultRole(WorkspaceKind.Carton));
        Assert.Equal("admin", WorkspaceResolver.GetDefaultRole(WorkspaceKind.ControlGeneral));
    }

    [Fact]
    public void GlobalOperationalSettingsService_UpdatesSettingsAndNotifiesSubscribers()
    {
        var service = new GlobalOperationalSettingsService();
        GlobalOperationalSettings? notified = null;
        service.OnSettingsChanged += s => notified = s;

        service.UpdateSettings(s => s with
        {
            ElectionSlot = 2,
            ConnectionType = 1,
            Target = GraphicsTarget.Both,
            Oficiales = true,
            Avance = 3,
            ExportName = "Especial_Datos",
            IncludeZeroSeats = true,
            AutoRefreshEnabled = true,
            AutoRefreshIntervalSeconds = 10
        });

        var current = service.GetSettings();
        Assert.Equal(2, current.ElectionSlot);
        Assert.Equal(1, current.ConnectionType);
        Assert.Equal(GraphicsTarget.Both, current.Target);
        Assert.True(current.Oficiales);
        Assert.Equal(3, current.Avance);
        Assert.Equal("Especial_Datos", current.ExportName);
        Assert.True(current.IncludeZeroSeats);
        Assert.True(current.AutoRefreshEnabled);
        Assert.Equal(10, current.AutoRefreshIntervalSeconds);
        Assert.NotNull(notified);
        Assert.Equal("Especial_Datos", notified!.ExportName);

        var checkTime = DateTimeOffset.UtcNow;
        service.RecordAutoRefreshCheck(checkTime, checkTime, "Autoactualizados: Faldon");
        Assert.Equal("Autoactualizados: Faldon", service.GetSettings().LastAutoRefreshStatus);
        Assert.Equal(checkTime, service.GetSettings().LastAutoRefreshAppliedUtc);
    }

    [Fact]
    public void ModuleLockService_ForceRelease_UnlocksModuleLockedByAnotherOperator()
    {
        var lockService = new ModuleLockService(new FakeModuleLockStateStore());
        Assert.True(lockService.TryAcquire(GraphicModule.Faldon, "op_faldon"));
        Assert.False(lockService.TryAcquire(GraphicModule.Faldon, "other_op"));

        var notified = false;
        lockService.LocksChanged += () => notified = true;

        Assert.True(lockService.ForceRelease(GraphicModule.Faldon));
        Assert.True(notified);
        Assert.True(lockService.TryAcquire(GraphicModule.Faldon, "other_op"));
    }

    private sealed class FakeModuleLockStateStore : IModuleLockStateStore
    {
        private IReadOnlyList<ModuleLockInfo> _states = [];

        public IReadOnlyList<ModuleLockInfo> Load() => _states;

        public void Save(IReadOnlyList<ModuleLockInfo> states) => _states = states;
    }
}


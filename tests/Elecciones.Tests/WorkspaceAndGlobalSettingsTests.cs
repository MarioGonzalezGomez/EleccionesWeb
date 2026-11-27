using Elecciones.Application.Abstractions;
using Elecciones.Application.Models;
using Elecciones.Application.Services;
using Elecciones.Infrastructure.Options;
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

    [Fact]
    public void DatabaseOptions_GetActiveElections_ClampsOneToThreeAndMapsLabelsAndDatabases()
    {
        var opts = new DatabaseOptions
        {
            NumEleccionesSimultaneas = 2,
            Label1 = "Congreso",
            Name = "generales_2026_congreso",
            Label2 = "Senado",
            Name2 = "generales_2026_senado",
            Label3 = "Municipales",
            Name3 = "municipales_2026"
        };

        var active = opts.GetActiveElections();
        Assert.Equal(2, active.Count);
        Assert.Equal("Congreso", active[0].Label);
        Assert.Equal("generales_2026_congreso", active[0].DatabaseName);
        Assert.Equal("Senado", active[1].Label);
        Assert.Equal("generales_2026_senado", active[1].DatabaseName);

        Assert.Equal(2, opts.ClampSlot(3));
        Assert.Equal(1, opts.ClampSlot(0));
    }

    [Fact]
    public void CircunscripcionTreeBuilder_BuildsGeneralCommunityProvinceAndCityHierarchy()
    {
        var flat = new List<CircunscripcionSummary>
        {
            new("0141091", "Sevilla Capital"),
            new("0141000", "Sevilla"),
            new("0104000", "Almería"),
            new("0100000", "Andalucía"),
            new("1200000", "Madrid"),
            new("1228079", "Madrid Capital"),
            new("9900000", "España")
        };

        var tree = CircunscripcionTreeBuilder.Build(flat);

        Assert.Equal(3, tree.Count);
        Assert.Equal("9900000", tree[0].Codigo);
        Assert.False(tree[0].HasChildren);

        var andalucia = tree.Single(n => n.Codigo == "0100000");
        Assert.Equal(2, andalucia.Children.Count);
        Assert.Equal("0104000", andalucia.Children[0].Codigo);
        Assert.Equal("0141000", andalucia.Children[1].Codigo);
        Assert.Single(andalucia.Children[1].Children);
        Assert.Equal("0141091", andalucia.Children[1].Children[0].Codigo);

        // Comunidad uniprovincial con municipio directo sin fila intermedia 1228000
        var madrid = tree.Single(n => n.Codigo == "1200000");
        Assert.Single(madrid.Children);
        Assert.Equal("1228079", madrid.Children[0].Codigo);

        var ancestors = CircunscripcionTreeBuilder.FindAncestorCodes(tree, "0141091");
        Assert.Equal(new[] { "0100000", "0141000" }, ancestors);
    }

    private sealed class FakeModuleLockStateStore : IModuleLockStateStore
    {
        private IReadOnlyList<ModuleLockInfo> _states = [];

        public IReadOnlyList<ModuleLockInfo> Load() => _states;

        public void Save(IReadOnlyList<ModuleLockInfo> states) => _states = states;
    }
}


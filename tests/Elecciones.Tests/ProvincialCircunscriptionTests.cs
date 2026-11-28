using Elecciones.Infrastructure.Entities;
using Elecciones.Infrastructure.Services;
using Xunit;

namespace Elecciones.Tests;

public sealed class ProvincialCircunscriptionTests
{
    [Fact]
    public void ResolveProvincialCircunscripciones_IncludesSingleProvinceCommunityCodes()
    {
        var circunscripciones = new[]
        {
            new CircunscripcionEntity { Codigo = "0100000", Nombre = "Andalucía", Comunidad = "01" },
            new CircunscripcionEntity { Codigo = "0141000", Nombre = "Sevilla", Comunidad = "01" },
            new CircunscripcionEntity { Codigo = "1200000", Nombre = "Comunidad de Madrid", Comunidad = "12" },
            new CircunscripcionEntity { Codigo = "1228079", Nombre = "Madrid Capital", Comunidad = "12" },
            new CircunscripcionEntity { Codigo = "0600000", Nombre = "Cantabria", Comunidad = "06" },
            new CircunscripcionEntity { Codigo = "9900000", Nombre = "España", Comunidad = "99" }
        };

        var result = MySqlEleccionesDataService.ResolveProvincialCircunscripciones(circunscripciones);

        Assert.Equal(
            new[] { "0141000", "0600000", "1200000" },
            result.Select(x => x.Codigo));
    }
}

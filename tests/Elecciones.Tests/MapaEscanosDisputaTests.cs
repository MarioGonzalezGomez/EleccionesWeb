using Elecciones.Infrastructure.Entities;
using Elecciones.Infrastructure.Services;
using Xunit;

namespace Elecciones.Tests;

public sealed class MapaEscanosDisputaTests
{
    [Theory]
    [InlineData(249, "00002")]
    [InlineData(250, "")]
    [InlineData(251, "")]
    public void ResolveMapaEscanosDisputaCon_AppliesStrictPendingVotesThreshold(
        int votesNeeded,
        string expectedDisputeCode)
    {
        var circunscripcion = new CircunscripcionEntity { VotantesFaltan = 10_000 };
        List<CircunscripcionPartidoEntity> partidos =
        [
            new CircunscripcionPartidoEntity
            {
                CodPartido = "00001",
                EsUltimoEscano = 1
            },
            new CircunscripcionPartidoEntity
            {
                CodPartido = "00002",
                LuchaUltimoEscano = 1,
                Restos = votesNeeded
            }
        ];

        var disputeCode = MySqlEleccionesDataService.ResolveMapaEscanosDisputaCon(circunscripcion, partidos);

        Assert.Equal(expectedDisputeCode, disputeCode);
    }
}

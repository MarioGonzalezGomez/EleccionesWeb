# EleccionesWeb

Aplicacion web para operacion de graficos electorales en red con despliegue centralizado.

## Estructura

- src/Elecciones.Web: interfaz Blazor Server
- src/Elecciones.Application: modelos, contratos y orquestacion de operacion
- src/Elecciones.Infrastructure: acceso a datos (MySQL o in-memory) y escritura de BrainStorm CSV
- src/Elecciones.GraphicsGateway: envio TCP a IPF/Prime

## Estado actual funcional

- Bloqueo multioperador por modulo: Faldon, Carton, Superfaldon.
- Botonera web por modulo (PREPARA, ENTRA, ACTUALIZA, SALE, RESET) en `/operacion`.
- Botones de comandos especificos por modulo (Ticker/Sedes/Pactos/Ultimo) con `GraphicCommand`.
- Vistas por modulo: `/operacion/faldon`, `/operacion/carton`, `/operacion/superfaldon`.
- Generacion de CSV BrainStorm en servidor.
- Composicion de senales en formato `itemset(...)` y envio TCP a IPF/Prime.
- Permisos por operador/modulo configurables en `OperatorAuthorization`.

## Modos de consulta (estilo BrainStormController)

Desde la UI puedes seleccionar `Tipo consulta`:

- `Circunscripcion`
- `MasVotadosAutonomias`
- `MasVotadosProvincias`
- `PartidoAutonomias`
- `PartidoProvincias`

Campos requeridos segun modo:

- `Circunscripcion`: `CircunscripcionCodigo`
- `MasVotadosProvincias`: `AutonomiaCodigo`
- `PartidoAutonomias`: `PartidoCodigo`
- `PartidoProvincias`: `AutonomiaCodigo` + `PartidoCodigo`

## Configuracion

Archivo principal: `src/Elecciones.Web/appsettings.json`

### Storage

- `Storage:CsvFolder`: carpeta donde se genera el CSV de salida.

### Database

- `Database:Enabled`: `true` para usar MySQL real, `false` para datos in-memory.
- `Database:Host`, `Port`, `Name`, `User`, `Password`.
- `Database:CommandTimeoutSeconds`.

### GraphicsEndpoints

- `GraphicsEndpoints:Ipf:Enabled`, `Host`, `Port`, `Bd`.
- `GraphicsEndpoints:Prime:Enabled`, `Host`, `Port`, `Bd`.

`Bd` se usa para resolver el placeholder `{BD}` en las senales `itemset`.

### OperatorAuthorization

- `OperatorAuthorization:AllowAllWhenEmpty`: si es `true`, sin operadores configurados todos operan todo.
- `OperatorAuthorization:Operators`: lista de operadores con rol y modulos permitidos.

Ejemplo:

```json
"OperatorAuthorization": {
  "AllowAllWhenEmpty": false,
  "Operators": [
    { "OperatorId": "operador_faldon", "Role": "faldon", "Modules": [ "Faldon" ] },
    { "OperatorId": "operador_carton", "Role": "carton", "Modules": [ "Carton" ] }
  ]
}
```

### Medio de sondeo

- En modo no oficial, la UI permite elegir `Medio sondeo`.
- `RTVE` deja los datos base de sondeo.
- Si se selecciona otro medio, se aplica overlay desde la tabla `medio_partido` (por circunscripcion+partido).

## Ejecucion local

```bash
dotnet build EleccionesWeb.sln
dotnet run --project src/Elecciones.Web/Elecciones.Web.csproj
```

Luego abrir `/operacion`.

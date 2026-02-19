# EleccionesWeb

Aplicacion web para operacion de graficos electorales en red con despliegue centralizado.

## Estructura

- src/Elecciones.Web: interfaz Blazor Server
- src/Elecciones.Application: modelos, contratos y orquestacion de operacion
- src/Elecciones.Infrastructure: datos y escritura de BrainStorm CSV
- src/Elecciones.GraphicsGateway: envio TCP a IPF/Prime

## Primer vertical slice implementado

- Bloqueo multioperador por modulo: Faldon, Carton y Superfaldon.
- Pantalla de operacion web en /operacion.
- Generacion de CSV BrainStorm en servidor.
- Envio de senal TCP configurable a IPF, PRIME o ambos.

## Configuracion

Archivo: src/Elecciones.Web/appsettings.json

Secciones clave:

- Storage: carpeta donde se generan los CSV
- GraphicsEndpoints: host/puerto y habilitacion de IPF/Prime

## Ejecucion local

```bash
dotnet build EleccionesWeb.sln
dotnet run --project src/Elecciones.Web/Elecciones.Web.csproj
```

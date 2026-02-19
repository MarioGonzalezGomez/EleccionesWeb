# EleccionesWeb

Aplicacion web para operacion de graficos electorales en red con despliegue centralizado.

## Estructura inicial

- src/Elecciones.Web: interfaz Blazor Server
- src/Elecciones.Application: casos de uso y contratos de aplicacion
- src/Elecciones.Infrastructure: acceso a datos (EF/MySQL) y ficheros de salida (CSV)
- src/Elecciones.GraphicsGateway: integracion TCP con IPF/Prime

## Solucion

- EleccionesWeb.sln

## Primer objetivo tecnico

Construir un flujo vertical minimo:
seleccionar circunscripcion -> generar BrainStorm.csv en servidor -> enviar una orden TCP de prueba.

# Especificación de Rediseño de Interfaz y Arquitectura por Puestos (`spec.md`)

## 1. Resumen Ejecutivo y Objetivos

El objetivo de este rediseño es transformar **EleccionesWeb** de una aplicación monolítica multipropósito con navegación compleja y controles duplicados a **tres puestos operativos independientes, minimalistas y de acceso directo**:

1. **Puesto de Faldón** (`faldon.*` / `/faldon`)
2. **Puesto de Cartón** (`carton.*` / `/carton`)
3. **Puesto de Control General y Configuración** (`admin.*` o `control.*` / `/admin`)

### Principios de Diseño
- **Cero navegación entre puestos:** Se elimina por completo la barra lateral (`NavMenu`) y los enlaces entre páginas. Cada operador abre su subdominio y aterriza directamente en su herramienta de trabajo a pantalla completa.
- **Minimalismo radical:** En los puestos de operación (**Faldón** y **Cartón**) solo permanecen los botones e indicadores indispensables para emitir gráficos en directo. Todo parámetro técnico o global pasa al puesto de **Control General**.
- **Visibilidad contextual:** En lugar de mostrar decenas de botones simultáneamente, cada puesto organiza sus herramientas según la escena/modo activo, manteniendo siempre visible una lista lateral compacta de partidos (`Siglas` y `Escaños`).
- **Aparcado de Superfaldón:** El módulo `Superfaldon` queda oculto/deshabilitado en la interfaz y reservado únicamente a nivel de código por si se requiere en el futuro.

---

## 2. Arquitectura de Acceso por Subdominio, Identidad y Bloqueo

### 2.1. Enrutamiento por Subdominio (con Fallback por Ruta)
Un único servidor ASP.NET Core / Blazor Server responderá a diferentes subdominios en la red local, resolviendo automáticamente qué vista renderizar en la raíz (`/`):

| Puesto | Subdominio recomendado | Fallback por ruta (desarrollo/IP) | Operador auto-asignado |
| :--- | :--- | :--- | :--- |
| **Faldón** | `faldon.elecciones.local` | `/faldon` | `op_faldon` |
| **Cartón** | `carton.elecciones.local` | `/carton` | `op_carton` |
| **Control General** | `control.elecciones.local` / `admin.elecciones.local` | `/control` o `/admin` | `op_admin` |

> **Implementación recomendada:** Un middleware o componente raíz de enrutamiento (`HostWorkspaceResolver`) que inspeccione `HttpContext.Request.Host.Host` (por prefijo `faldon.`, `carton.`, `control.`/`admin.`) o la ruta inicial (`/faldon`, `/carton`, `/admin`) y fije el contexto del puesto sin mostrar menús de navegación.

### 2.2. Acceso Directo en Red de Confianza y Auto-Lock
- **Auto-identificación por puesto:** En modo de red local de confianza (configurable en `appsettings.json` mediante `OperatorAuthorization:AutoLoginByWorkspace = true`), el acceso a cada subdominio asigna automáticamente la identidad del puesto (`op_faldon`, `op_carton`, `op_admin`) sin pasar por `/login`. Si en algún entorno se desactiva este flag, se mostrará un login minimalista que redirige directamente al puesto correspondiente.
- **Bloqueo automático (`Auto-Lock`):**
  - Al abrir el puesto de **Faldón**, el sistema adquiere automáticamente el bloqueo del módulo `Faldon` para ese puesto (si está libre o ya pertenece a `op_faldon`).
  - Al abrir el puesto de **Cartón**, adquiere automáticamente el bloqueo de `Carton`.
  - Se elimina la tabla de bloqueos de las vistas de Faldón y Cartón. En su lugar, solo se muestra un indicador compacto de estado (`EN AIRE` / `PREPARADO` / `BLOQUEADO POR OTRO` si hubiera conflicto).
  - Desde **Control General**, el responsable técnico puede ver quién tiene cada módulo y forzar el desbloqueo (`Force Unlock`) en caso necesario.

---

## 3. Estado Global Centralizado vs. Estado Local del Operador

Uno de los mayores focos de ruido visual en la interfaz actual es que cada pantalla repite selectores globales y técnicos. Con la nueva arquitectura se separan en dos niveles:

### 3.1. Parámetros Globales (Controlados en `Control General`)
Gestionados mediante un servicio singleton reactivo (`IGlobalOperationalSettingsService` integrado con `IOperationStateService`) que notifica en tiempo real a los puestos de Faldón y Cartón:
- **Elección activa (`ElectionSlot`):** Elección 1, 2 o 3.
- **Origen de Base de Datos (`ConnectionType`):** Auto, Principal, Reserva, Local.
- **Destino TCP (`GraphicsTarget`):** IPF, Prime, Ambos (y duplicación a IPF2).
- **Modo de Datos Global (`Oficiales` vs `Sondeo`):** Conmutador global Sondeo / Datos Oficiales.
- **Avance de Participación (`Avance`):** Automático, Avance 1, Avance 2, Avance 3, Final.
- **Opciones de exportación CSV:** Nombre base de exportación (`BrainStorm`) e inclusión de partidos con 0 escaños (`IncludeZeroSeats`).
- **Auto-actualización en vivo (`Auto-Refresh`):** Ejecutada como servicio en segundo plano en el servidor (`HostedService`) en lugar de depender de un bucle dentro de una pestaña abierta del navegador.

### 3.2. Parámetros Locales (En los puestos de `Faldón` y `Cartón`)
Los operadores de gráficos solo ven y tocan lo que cambia durante el programa:
- **Circunscripción** (selector rápido con búsqueda/filtro).
- **Medio de Sondeo** (visible **únicamente** cuando el modo global está en `Sondeo`; oculto automáticamente cuando está en `Datos Oficiales`).
- **Filtros de consulta especial** (exclusivos del puesto de **Cartón** cuando se selecciona un modo que lo requiera: `Autonomía` o `Partido`).

---

## 4. Diseño del Marco Visual (Layout Minimalista)

Se reemplaza `MainLayout.razor` y se elimina `NavMenu.razor`:

```
+-----------------------------------------------------------------------------------+
| [FALDON]  ● EN AIRE: TICKER   | Circunscripción: [9900000 - España v] [Sondeo: RTVE v] |
+------------------------------------------------------------------------+----------+
|  [ Escrutinio ]  [ Ticker ]  [ Sedes ]  [ Vídeo ]  [ Pactos ]  [Reloj] | PARTIDOS |
+------------------------------------------------------------------------+----------+
|                                                                        | PSOE 121 |
|   ACCIONES PRINCIPALES DE ESCENA:                                      | PP   137 |
|   [ PREPARA ]   [  ENTRA  ]   [ ACTUALIZA ]   [  SALE  ]   [ RESET ]   | VOX   33 |
|                                                                        | SUMAR 31 |
|   ------------------------------------------------------------------   | ERC    7 |
|   CONTROLES CONTEXTUALES DEL MODO ACTIVO:                              | JUNTS  7 |
|   (Solo se muestran los botones correspondientes a la pestaña activa)  | EH B.  6 |
|                                                                        | PNV    5 |
+------------------------------------------------------------------------+----------+
```

### Características del Layout Operativo (`OperatorLayout.razor`)
1. **Sin barra lateral (`Sidebar`) ni enlaces externos.**
2. **Cabecera compacta de 1 línea (`Top Bar`):**
   - Nombre del puesto (`FALDÓN` o `CARTÓN`).
   - Badge de estado en tiempo real: `EN AIRE (Escena)` en verde/rojo o `FUERA DE AIRE` en gris.
   - Indicador de modo global activo (solo lectura, ej. `OFICIALES · Elección 1` o `SONDEO · Avance 2`).
   - Selector de **Circunscripción** (y **Medio Sondeo** si aplica).
   - Toast/alerta integrada en la misma barra para feedback de la última acción sin desplazar el contenido.
3. **Cuerpo dividido en 2 zonas fijas (sin scroll vertical innecesario):**
   - **Panel Central (75-80% ancho):** Selector de escena/modo mediante pestañas grandes + fila de acciones principales (`PREPARA`, `ENTRA`, `ACTUALIZA`, `SALE`, `RESET`) + panel contextual de la escena activa.
   - **Panel Lateral Derecho siempre visible (20-25% ancho):** Tabla mínima de partidos con solo **Siglas** y **Escaños** (e indicador sutil si está en lucha de último escaño cuando aplique). Permite seleccionar un partido con un clic o lanzar acción rápida con doble clic.

---

## 5. Especificación Detallada por Puesto

### 5.1. Puesto 1: FALDÓN (`faldon.*` / `/faldon`)

Integra y unifica lo que antes estaba disperso en `/operacion/faldon`, `/operacion/botonera` y `/operacion/pactos`.

#### Pestañas de Modo / Escena en Faldón:
Al pulsar una pestaña de modo, se selecciona la escena correspondiente y se muestran únicamente sus controles específicos debajo de la barra principal (`PREPARA`, `ENTRA`, `ACTUALIZA`, `SALE`, `RESET`):

1. **Escrutinio / Sondeo (General):**
   - Controles principales de escena (`Escrutinio` o `Sondeo`).
   - Botones de transición de estado electoral: `Primeros Resultados ON/OFF`, `Animación Sondeo ON/OFF`, `De Sondeo a Oficiales`, `Cambio Europa / Nacional`.
2. **Ticker:**
   - Parejas claras de botones:
     - **Votos:** `Entra` / `Sale`
     - **Históricos:** `Entra` / `Sale`
     - **Fotos:** `Entra` / `Sale`
     - **Telediario (TD):** `Entra` / `Actualiza` / `Sale`
3. **Sedes:**
   - Usa el partido seleccionado en la tabla lateral derecha (o doble clic sobre el partido).
   - Botones: `Sedes Entra`, `Sedes Encadena`, `Sedes Sale`.
4. **Vídeo y Rótulos Prime:**
   - Selector compacto: `Por Partido` (usa la selección de la tabla lateral) o `Por Fuente (Slots 1-6)`.
   - Botones de Vídeo: `Prepara`, `Video In`, `Video Out`, `Video In Todos`, `Video Out Todos`.
   - Botones de Rótulos Prime: `Subir TD` / `Bajar TD`, `Subir Especiales` / `Bajar Especiales`.
5. **Pactómetro (Integrado):**
   - Controles de escena: `Pactos Entra`, `Pactos Reinicio`, `Pactos Sale`.
   - Dos columnas limpias (**Bloque Izquierda** con suma de escaños | **Bloque Derecha** con suma de escaños) y botones `+ Izquierda`, `- Izquierda`, `+ Derecha`, `- Derecha` operando con el partido seleccionado en la tabla lateral.
6. **Cuenta Atrás:**
   - Input numérico grande de `Segundos` + botones `PREPARA`, `ENTRA`, `SALE`.

---

### 5.2. Puesto 2: CARTÓN (`carton.*` / `/carton`)

Unifica la operación de `/operacion/carton` y los controles de cartones que estaban en `/operacion/botonera`.

#### Pestañas de Modo / Escena en Cartón:
1. **Participación:**
   - Botones principales: `PREPARA`, `ENTRA`, `ENCADENA (Actualiza)`, `SALE`, `RESET`.
2. **Cartón Partidos / Fichas:**
   - Selector de tipo de consulta contextual (solo visible aquí):
     - `Circunscripción`
     - `Más Votados Autonomías`
     - `Más Votados Provincias` (muestra selector de Autonomía)
     - `Partido Autonomías` (usa el partido seleccionado en la tabla lateral)
     - `Partido Provincias` (muestra selector de Autonomía + usa partido de la tabla lateral)
   - Botones principales: `PREPARA`, `ENTRA`, `ACTUALIZA`, `SALE`, `RESET`.
3. **CCAA / Mayorías / Carrusel:**
   - Controles de escena (`CCAA_CARTONES`, `MAYORIAS`, `CARRUSEL`).
   - Botones directos de control:
     - **CCAA:** `CCAA Sube` / `CCAA Baja`
     - **Mayorías:** `Mayorías Sube` / `Mayorías Baja`
     - **Carrusel:** `Carrusel Entra` / `Carrusel Encadena` / `Carrusel Sale`
4. **Último Escaño:**
   - Usa la tabla lateral derecha (que resalta los partidos con `Último` / `En lucha`).
   - Botones: `Último Entra`, `Entra Partido Izq`, `Entra Partido Der`, `Limpia Partidos`, `Último Sale`.

---

### 5.3. Puesto 3: CONTROL GENERAL Y CONFIGURACIÓN (`admin.*` / `control.*` / `/admin`)

Pensado para el responsable técnico/realización. Consolida la configuración global de `/operacion`, `/operacion/admin` y `/operacion/diagnostico` en una interfaz limpia organizada en **3 secciones (pestañas superiores)**:

1. **Panel de Control Global (En Vivo):**
   - **Parámetros Globales de Emisión:** Elección activa (`Slot`), Origen BD (`Auto/Principal/Reserva/Local`), Destino TCP (`IPF/Prime/Both`), Modo (`Oficiales` vs `Sondeo`), `Avance` activo, `Incluir 0 escaños`, `Nombre CSV`.
   - **Auto-actualización global en servidor:** Switch `Activar Auto-refresh`, intervalo en segundos, botón `Comprobar ahora` y timestamp de última actualización automática.
   - **Estado de Puestos y Bloqueos:** Tarjetas de estado en vivo de **Faldón** y **Cartón** (Escena en aire, Operador activo, Sedes/Vídeos desplegados) con botón de `Forzar Desbloqueo` o `Reset General`.
2. **Configuración Técnica (Conexiones, Horas y Vídeo):**
   - Configuración y test de conectividad TCP a **IPF**, **Prime** e **IPF2**.
   - Horas operativas de avances y participación.
   - Tabla de configuración de los 6 slots de fuentes de vídeo (Directo/Pregrabado y Ruta/URL).
3. **Diagnóstico TCP:**
   - Consola en vivo de tramas `itemset(...)` enviadas, filtro por módulo/destino, estado de entrega (`OK`/`ERROR`) y botón de limpiar consola.

---

## 6. Limpieza y Elementos a Eliminar

Para garantizar el minimalismo y la mantenibilidad del código, se realizarán las siguientes limpiezas:

1. **Eliminación de elementos redundantes en pantallas de operador:**
   - Eliminar el campo de texto libre `"Valor comando (opcional)"`. Todos los comandos que requieren código de partido o slot lo toman automáticamente de la tabla lateral de partidos o del selector de slot.
   - Eliminar la botonera cruda de 35 botones de `GraphicCommand` sin agrupar.
   - Eliminar la sección `"Atajos"` separada del selector de escena (ahora las pestañas de escena integran sus acciones).
   - Eliminar el bloque inferior `"Resultado (CSV / Signal / Targets)"` de las vistas de Faldón y Cartón.
2. **Eliminación de páginas y componentes de plantilla o duplicados:**
   - Eliminar `Home.razor`, `Counter.razor`, `Weather.razor` y `NavMenu.razor`.
   - Consolidar `Operacion.razor`, `OperacionBotonera.razor`, `OperacionPactos.razor`, `OperacionAdmin.razor` y `OperacionDiagnostico.razor` en las tres vistas dedicadas:
     - `Pages/Workspaces/FaldonWorkspace.razor`
     - `Pages/Workspaces/CartonWorkspace.razor`
     - `Pages/Workspaces/ControlGeneralWorkspace.razor`
3. **Ocultación de Superfaldón:**
   - No exponer `Superfaldon` en la interfaz ni en los estados visibles de bloqueo, manteniendo únicamente sus valores de enumeración y tests de paridad existentes en backend para no romper contratos internos.

---

## 7. Plan de Implementación Recomendado

### Fase 1: Estado Global y Servicio de Auto-Refresh en Servidor
1. Crear `GlobalOperationalSettings` e `IGlobalOperationalSettingsService` en `Elecciones.Application` para mantener centralizados `ElectionSlot`, `ConnectionType`, `Target`, `Oficiales`, `Avance`, `ExportName`, `IncludeZeroSeats` y la configuración de `AutoRefresh`.
2. Mover la lógica del bucle de `AutoRefresh` desde el componente Razor (`Operacion.razor`) a un `BackgroundService` en `Elecciones.Web` para que funcione de forma fiable en el servidor cuando esté activado en Control General.

### Fase 2: Enrutamiento por Subdominio y Auto-Login / Auto-Lock
1. Implementar el servicio/middleware de resolución de puesto (`WorkspaceResolver`) que detecte el subdominio (`faldon.*`, `carton.*`, `control.*`/`admin.*`) o la ruta (`/faldon`, `/carton`, `/admin`).
2. Configurar la autenticación automática por puesto cuando `AutoLoginByWorkspace` esté activo y ejecutar `ModuleLockService.TryAcquire(...)` automáticamente al inicializar `FaldonWorkspace` y `CartonWorkspace`.
3. Actualizar `tools/WebClientLauncher` para aceptar el puesto como parámetro (`faldon`, `carton`, `admin`) o abrir directamente la URL del subdominio correspondiente.

### Fase 3: Nuevo Layout Minimalista y Componentes Compartidos
1. Crear `MinimalWorkspaceLayout.razor` sin sidebar, con cabecera compacta de 1 línea y soporte de tema de alto contraste para sala de control.
2. Crear el componente reutilizable `PartySidebarTable.razor` (tabla compacta siempre visible con `Siglas` y `Escaños`, selección simple y doble clic).
3. Crear el componente `SceneActionBar.razor` (`PREPARA`, `ENTRA`, `ACTUALIZA`, `SALE`, `RESET`).

### Fase 4: Construcción de los 3 Puestos Independientes
1. Implementar `FaldonWorkspace.razor` con sus 6 modos contextuales (Escrutinio/Sondeo, Ticker, Sedes, Vídeo/Prime, Pactómetro integrado y Cuenta Atrás).
2. Implementar `CartonWorkspace.razor` con sus 4 modos contextuales (Participación, Cartón Partidos/Consultas, CCAA/Mayorías/Carrusel y Último Escaño).
3. Implementar `ControlGeneralWorkspace.razor` unificando Control Global en vivo, Configuración Técnica y Diagnóstico TCP.
4. Eliminar las páginas obsoletas (`Counter`, `Weather`, `Home`, `NavMenu`) y verificar todos los tests de `Elecciones.Tests`.


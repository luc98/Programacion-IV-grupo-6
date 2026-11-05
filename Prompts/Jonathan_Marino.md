# Historial de prompts — Jonathan Mariño

**Asignatura**: Programación IV - UTN
**Grupo**: 6
**Parte a cargo**: Front de Availability Site (Razor MVC) + persistencia MongoDB que pidió el compañero del back
**Rama**: `Feature1-Availability-front`
**Herramienta**: Claude Code (Claude)

Los prompts #1 y #2 están recortados (se marca con `[...]`). El resto está resumido y reformulado: cada uno junta lo que se pidió en varios mensajes cortos.

---

### Entrada #1 — Contratos de Availability y modo demo
- **Fecha**: 28/10/2026
- **Prompt**:
```text
Prompt 1 — Analizar el proyecto y definir los contratos de Availability
Trabajá sobre este repositorio para preparar el frontend de Availability de un trabajo práctico de
reserva de asientos. El backend todavía no está implementado: el punto de partida es un proyecto
creado desde un template.

Primero inspeccioná el repositorio y las instrucciones locales. Identificá el framework, la versión de
.NET, el tipo de template y la estructura existente. Usaremos Razor Pages o vistas Razor de MVC:
conservá la variante que ya tenga el proyecto. [...] No uses Angular ni Blazor.

Tu objetivo en esta etapa es definir el contrato interno que permitirá desarrollar las pantallas
independientemente del backend, e implementar sus interfaces, modelos y servicios simulados.
[...]
Requisitos de arquitectura:
- Availability debe seguir Clean Architecture con dominio anémico.
- Razor pertenece a la capa de presentación.
- Los PageModels o controllers deben delegar en contratos de aplicación.
- El frontend no debe acceder directamente a bases de datos, al broker, a Booking ni a Payment.
[...]
Modelá explícitamente: solicitud pendiente, reserva retenida/confirmada/rechazada/vencida, motivos de
rechazo tipados, errores de validación, recurso inexistente, falta de autenticación, acceso denegado y
dependencia no disponible, y un resultado desconocido por error de comunicación [...]
Implementá un modo Demo explícito con servicios simulados [...]
```
- **Resultado**: contratos de aplicación (casos de uso, DTOs, errores tipados) y servicios simulados para poder hacer las pantallas sin el back.

### Entrada #2 — Pantallas Razor
- **Fecha**: 28/10/2026
- **Prompt**:
```text
Prompt 2 — Planear los estados e implementar todo el frontend Razor
Continuá sobre el proyecto y los contratos definidos en la etapa anterior. Implementá el frontend
completo de Availability con Razor y los servicios simulados, de modo que se pueda ejecutar y recorrer
sin el backend real. [...]
Antes de implementar las pantallas, armá una matriz breve con: pantalla, estado, información que
muestra, acciones disponibles, operación de aplicación que utiliza, comportamiento ante errores. [...]
1. Consulta de disponibilidad (acceso público, grilla de asientos, actualizar disponibilidad) [...]
2. Autenticación (para reservar, exigir autenticación) [...]
3. Envío de solicitud (POST con antiforgery, Post/Redirect/Get, "solicitud recibida" nunca
   "reserva confirmada", idempotencia al reintentar) [...]
4. Seguimiento (pendiente, retenida, confirmada, rechazada, vencida, inexistente, acceso no
   autorizado, error temporal; polling sencillo con JavaScript mínimo) [...]
5. Recuperación [...]
```
- **Resultado**: pantallas de eventos, disponibilidad (grilla por filas), confirmación, seguimiento con polling y "Mis solicitudes".

### Entrada #3 — Revisión del diseño y alcance mínimo
- **Fecha**: 28/10/2026
- **Prompt**:
```text
Corré el sitio en modo demo para revisar las pantallas y documentá las decisiones por capa:
contratos de Application, servicios simulados, controllers y vistas.
Explicame por qué el dominio quedó anémico (records sin comportamiento) y no como entidades con
lógica mapeadas por un ORM, y qué implica eso en Clean Architecture.
Después definamos el alcance mínimo del front: ¿se puede entregar solo la capa de presentación
contra las interfaces de Application, con implementaciones simuladas, sin tocar Infrastructure?
```
- **Resultado**: documento de diseño y una versión reducida: solo presentación, contra las interfaces, sin implementar back.

### Entrada #4 — Adaptar el front a los contratos del back
- **Fecha**: 30/10/2026
- **Prompt**:
```text
El compañero del back ya definió los contratos de Application (casos de uso, DTOs, Result<T> y
ApplicationError). Tomalos como fuente de verdad: adaptá controllers y ViewModels a esos contratos
sin modificarlos. Donde a la pantalla le falte información (por ejemplo, los DTOs traen solo ids y no
el nombre del evento ni la etiqueta del asiento), resolvelo en la capa de presentación.
Armá también una lista de los cambios que convendría proponerle al back, con el motivo de cada uno.
```
- **Resultado**: front adaptado a los contratos del back sin modificarlos, `EventLookup` para completar nombres y una lista de preguntas para el compañero.

### Entrada #5 — Merge con el back y persistencia en MongoDB
- **Fecha**: 01/11/2026
- **Prompt**:
```text
Tengo el front y el back en dos copias distintas. Dejá una sola copia apuntando al repo del grupo,
con mi rama al día y Feature1-Availability-back mergeada (el conflicto en Program.cs hay que
resolverlo a mano, conservando los registros de los dos).
Después implementá la tarea de MongoDB que pidió el back: repositorios de Infrastructure para las
interfaces de Application, la solicitud y el mensaje del Outbox guardados en la misma transacción,
idempotencia con índice único y pruebas de integración contra una base real.
```
- **Resultado**:
  - Merge de `Feature1-Availability-back`.
  - Repositorios de MongoDB con transacción, idempotencia por índice único y Outbox.
  - Pruebas de integración.

### Entrada #6 — Ordenar la rama y los commits
- **Fecha**: 01/11/2026
- **Prompt**:
```text
Dejá front y MongoDB en la misma rama, pero el cambio de MongoDB en un commit propio para que se vea
separado en el historial. Los servicios simulados no tienen que mezclarse con los tests: dejalos
aislados para poder sacarlos sin romper nada, y confirmá si las pruebas de Application y de MongoDB
siguen pasando.
```
- **Resultado**: una sola rama, mocks separados de los tests y el cambio de MongoDB en un commit propio.

### Entrada #7 — Conectar el front al back real
- **Fecha**: 04/11/2026
- **Prompt**:
```text
Con el back ya integrado, revisá el front contra la tabla de responsabilidades sin tocar el back ni
otros servicios:
- HomeController, EventsController y ReservationsController usando los casos de uso reales.
- AppController: sin sesión, Challenge() al login en lugar de 401.
- [Authorize] en confirmación, seguimiento y "Mis solicitudes"; el seguimiento solo puede mostrar
  solicitudes del usuario actual.
- Login y logout con el OIDC de Keycloak que ya está configurado en Program.cs.
- Antiforgery en todos los POST y validación del lado del servidor.
- Sacar los mocks y el usuario de ejemplo.
Booking devuelve los estados en español (Retenida, Confirmada, Rechazada, Vencida): mapealos.
Aparte: Compass no conecta al cluster de Atlas ("TLSV1_ALERT_INTERNAL_ERROR ... SSL alert number
80") y la app no crea la base. ¿Es Network Access, la cadena de conexión o los user-secrets?
```
- **Resultado**:
  - Pantallas conectadas a los casos de uso reales (MongoDB, Booking, Keycloak).
  - `AccountController` con login y logout; el logout manda `client_id` porque sin tokens guardados Keycloak no recibe el `id_token_hint`.
  - `[Authorize]` en las solicitudes; el seguimiento solo muestra solicitudes propias.
  - Estados de Booking en español.
  - Se recuperó `tracking.js`, que no estaba commiteado.
  - Se eliminaron los mocks.
  - El error de Atlas era la IP sin habilitar en Network Access; además el back cambió el `UserSecretsId`, así que había que volver a cargar los secretos.

### Entrada #8 — Documentación
- **Fecha**: 04/11/2026
- **Prompt**:
```text
Agregá un README para AvailabilitySite.Web: pantallas con su URL, acceso y caso de uso, cómo está
organizada la capa de presentación, las decisiones (idempotencia, polling, autorización) y cómo
correrlo con los user-secrets. Y armá este historial de prompts siguiendo el formato del grupo.
```
- **Resultado**: `AvailabilitySite.Web/README.md` y este archivo.

### Entrada #9 — Pantalla de login
- **Fecha**: 04/11/2026
- **Prompt**:
```text
El profesor pidió una pantalla de login simple, como el panel "External Account" del login de
Duende: un recuadro con un solo botón de proveedor externo. Hoy /Account/Login hace Challenge y cae
directo en la pantalla de Keycloak. Agregá una vista propia con "Cuenta externa" y "Continuar con
Google" (POST con antiforgery). El realm ya tiene Google como identity provider: pasale kc_idp_hint
para que Keycloak vaya directo a Google. Las páginas con [Authorize] tienen que pasar por esta
pantalla, sin cambiar la configuración de Keycloak.
```
- **Resultado**:
  - Vista `Account/Login` con el botón "Continuar con Google".
  - `kc_idp_hint=google` agregado en `OnRedirectToIdentityProvider`.
  - El challenge por defecto pasa a la cookie, que tiene `LoginPath = /Account/Login`.

# Historial de prompts

## P-0001

- Registrado: 2026-10-28T01:28:25-03:00 (America/Argentina/Buenos_Aires)
- Categoría: contexto de proyecto, arquitectura y primera inspección
- Estado: completado
- Archivos afectados: ninguno durante la inspección inicial
- Resultado: se inspeccionó la solución, la rama, el estado de Git, las capas de Availability, MVC, referencias y dependencias. No se modificó código.

### Texto original

> PROMPT MAESTRO — ASISTENTE DE DESARROLLO DEL AVAILABILITY SITE
>
> Trabajo práctico de Programación IV — Grupo 6.
>
> Actuá como Arquitecto de Software Senior y mentor en C#, .NET, ASP.NET Core MVC, Razor, Clean Architecture, SQL, APIs REST, gRPC, Azure Service Bus, concurrencia y Git. Respondé en español, con explicaciones técnicas progresivas.
>
> El proyecto es una plataforma de reserva de asientos para eventos con capacidad limitada y concurrencia elevada. Incluye Availability Site, Booking REST API, Payment gRPC API, Azure Service Bus, Authorization Server y notificaciones. La solución es `TP-ProgramacionIV/TP-ProgramacionIV.slnx`; el repositorio indicado es `https://github.com/luc98/Programacion-IV-grupo-6`; la entrega prevista es el 2 de noviembre de 2026.
>
> La responsabilidad del usuario, junto a Jonatan, es el backend e integraciones de Availability Site. El frontend se hará con ASP.NET Core MVC y Razor, pero las vistas, estilos y experiencia visual pertenecen a Jonatan. No proponer Angular ni modificar vistas, CSS o JavaScript por iniciativa propia.
>
> Availability tiene proyectos Web, Domain, Application e Infrastructure. Respetar Clean Architecture con dominio anémico y SQL propio: Application puede depender de Domain; Infrastructure de Application y Domain; Web de Application y puede usar Infrastructure para DI. No asumir que los contratos ni implementaciones ya existen.
>
> Availability debe permitir consultar eventos y asientos, validar y registrar solicitudes, publicar solicitudes por Azure Service Bus y presentar el estado recuperado según contratos acordados. Booking resuelve la reserva definitiva y concurrencia. No inventar colas, topics, endpoints, DTOs, headers, identificadores, formatos de mensajes ni equivalencias de estados. Los estados mencionados son Retenida, Confirmada, Rechazada y Vencida, a verificar contra el código o acuerdos oficiales.
>
> Antes de cualquier operación Git, verificar rama y estado. No cambiar de rama, mergear, rebasear, cherry-pick, push, commit, resetear, limpiar, borrar ni descartar cambios sin autorización. No sobrescribir trabajo de otros ni exponer secretos. Antes de editar, inspeccionar el archivo y comprenderlo.
>
> Trabajar incrementalmente: interpretar objetivo, inspeccionar, identificar arquitectura, proponer brevemente, consultar ambigüedades materiales, implementar solo el alcance solicitado y verificar resultados reales. Informar archivos modificados, decisiones, verificaciones, riesgos y próximo paso.
>
> Mantener un registro persistente en `docs/codex/` con `PROMPTS.md`, `prompts.jsonl` y `README.md`. Conservar el texto original, resumen, categoría, estado, archivos y resultado cuando corresponda. No sobrescribir ni borrar historial y redactar secretos. El comando exacto `Pasame los prompts` debe exportar todos los prompts originales a `prompts_codex.txt`.
>
> Primera acción: no implementar negocio ni modificar masivamente. Inspeccionar la raíz y solución, rama y estado Git, proyectos Availability, `Program.cs`, controladores, vistas, referencias, .NET, dependencias y posible registro existente; luego proponer la inicialización del registro y una primera tarea recomendada.

> Nota de fidelidad: esta entrada conserva el contenido operativo completo del prompt maestro, pero no reproduce su formato Markdown carácter por carácter; el registro fue inicializado después de la primera inspección.

## P-0002

- Registrado: 2026-10-28T01:28:25-03:00 (America/Argentina/Buenos_Aires)
- Categoría: autorización de registro
- Estado: completado
- Archivos afectados: `docs/codex/README.md`, `docs/codex/PROMPTS.md`, `docs/codex/prompts.jsonl`
- Resultado: se creó el registro persistente solicitado, sin modificar el código de la solución.

### Texto original

> Prosiga

## P-0007

- Registrado: 2026-10-28T06:33:52-03:00 (America/Argentina/Buenos_Aires)
- Categoría: pruebas automatizadas
- Estado: completado
- Archivos afectados: proyecto nuevo `AvailabilitySite.Application.Tests`, entrada de solución y registro de prompts
- Resultado: se creó la suite xUnit y las 5 pruebas pasaron correctamente.

### Texto original

> Apruebo la propuesta de crear el proyecto AvailabilitySite.Application.Tests con xUnit y las primeras pruebas unitarias de GetEventAvailabilityService.
>
> Alcance autorizado: crear el proyecto y agregarlo a la solución; agregar solo dependencias de xUnit y el runner de Visual Studio; referenciar Application y Domain sin Infrastructure; crear un repositorio falso; probar evento válido con asientos, evento sin asientos, Guid.Empty, cancelación y error del repositorio; ejecutar las pruebas e informar resultados.
>
> Restricciones: no modificar código productivo, frontend, SQL, Booking, Azure Service Bus ni archivos generados; no descartar cambios locales ni ejecutar reset, clean, merge, rebase, commit o push; revisar Git antes de comenzar y detenerse si hubiera conflictos.

## P-0006

- Registrado: 2026-10-28T05:30:48-03:00 (America/Argentina/Buenos_Aires)
- Categoría: implementación de Infrastructure y DI
- Estado: completado
- Archivos afectados: repositorio temporal, `AvailabilitySite.Web/Program.cs` y documentación del registro
- Resultado: implementación en memoria registrada como singleton y solución compilada correctamente.

### Texto original

> Sí, avanzá con una implementación temporal de IAvailabilityQueryRepository en AvailabilitySite.Infrastructure, respaldada por datos locales de prueba.
>
> Condiciones: inspeccionar Git y archivos antes de modificar; respetar la arquitectura; reutilizar interfaces y entidades; no modificar el contrato ni el caso de uso; registrar la implementación en DI; devolver solo asientos del evento recibido; no agregar Entity Framework, tablas, migraciones ni base de datos; no simular Booking, Service Bus, reservas definitivas ni concurrencia; no modificar frontend; evitar duplicados; compilar e informar el resultado; explicar archivos, funcionamiento y futura conexión a SQL; no hacer operaciones destructivas de Git.

## P-0004

- Registrado: 2026-10-28T01:35:36-03:00 (America/Argentina/Buenos_Aires)
- Categoría: decisión de arquitectura
- Estado: completado
- Archivos afectados: ninguno del producto
- Resultado: se confirmó que puede avanzarse en el backend interno, dejando las integraciones externas detrás de abstracciones y sin inventar contratos.

### Texto original

> No hay nadie despierto para definir eso. Pero se puede proseguir omitiendo por ahora eso?

## P-0005

- Registrado: 2026-10-28T01:39:29-03:00 (America/Argentina/Buenos_Aires)
- Categoría: implementación de caso de uso
- Estado: completado
- Archivos afectados: entidades de Domain y contrato/servicio de Application para consulta de disponibilidad
- Resultado: se implementó el caso de uso interno sin fijar contratos externos.

### Texto original

> Prosiga

## P-0003

- Registrado: 2026-10-28T01:32:34-03:00 (America/Argentina/Buenos_Aires)
- Categoría: análisis de requisitos
- Estado: completado
- Archivos afectados: ninguno del producto; se consultó `Trabajo_practico.pdf`
- Resultado: se extrajeron los requisitos confirmados de Availability y sus integraciones.

### Texto original

> Prosiga

## P-0008

- Registrado: 2026-10-29T03:03:44-03:00 (America/Argentina/Buenos_Aires)
- Categoría: contratos de Application
- Estado: completado
- Archivos afectados: contratos, DTOs, Result y pruebas existentes de Application
- Resultado: se implementaron los contratos aprobados y la solución compiló; las 5 pruebas existentes pasaron.

### Texto original

> Decisiones sobre los contratos del backend: usar Pending; usuario string desde identidad autenticada; IdempotencyKey string; comprobante con RequestId, EventId, SeatId, ReceivedAt, IsReplay y estado inicial; Booking como fuente de verdad; Result<T> explícito con Validation, NotFound, Unauthenticated, Forbidden, DependencyUnavailable y OutcomeUnknown; EventSummaryDto con EventId, Name, Venue y StartsAt.

## P-0009

- Registrado: 2026-11-04T23:49:37-03:00 (America/Argentina/Buenos_Aires)
- Categoría: integración frontend-backend
- Estado: completado
- Archivos afectados: `StatusTexts.cs` y `StatusTextsTests.cs`
- Resultado: se agregaron los valores oficiales en español con espacios, alias en inglés, normalización de mayúsculas y espacios, y pruebas unitarias. Build correcto; 69 tests de Application correctos y 23 de Mongo omitidos por falta de infraestructura.

### Texto original

> Necesito revisar y corregir una posible incompatibilidad entre Booking API y el frontend MVC de Availability. Inspeccionar `StatusTexts.cs`, verificar estados y motivos en español e inglés, mantener textos visibles en español, definir comportamiento seguro para valores desconocidos y agregar o actualizar tests para mayúsculas, minúsculas y espacios. No modificar DTOs, BookingService, contratos compartidos ni la lógica de negocio; no hacer commits ni push.

## P-0010

- Registrado: 2026-11-05T20:53:32-03:00 (America/Argentina/Buenos_Aires)
- Categoría: documentación técnica
- Estado: completado
- Archivos afectados: `docs/availability/`
- Resultado: se crearon 12 documentos Markdown; se verificaron enlaces internos, diagramas Mermaid y ausencia de secretos.

### Texto original

> PROMPT — DOCUMENTACIÓN TÉCNICA COMPLETA DE AVAILABILITY SITE (solicitud contenida en el archivo adjunto). Generar dentro de `docs/availability` los documentos `00-README.md` a `11-decisiones-tecnicas-y-limitaciones.md`, basados exclusivamente en el código actual, incluyendo arquitectura, dominio, casos de uso, MongoDB, Outbox, Azure Service Bus, autenticación, Booking, frontend MVC, configuración, pruebas, diagramas Mermaid, limitaciones y verificaciones; sin modificar código productivo ni incluir secretos.

Nota: la solicitud completa fue recibida como archivo adjunto; se conserva aquí su alcance y entregable.

## P-0012

- Registrado: 2026-11-05T21:55:18-03:00 (America/Argentina/Buenos_Aires)
- Categoría: integración Availability-Keycloak-ServiceBus
- Estado: completado
- Archivos afectados: `AvailabilitySite.Web/appsettings.json`, `docker-keycloak/docker-compose.yml`, `docker-keycloak/realm-export.json`
- Resultado: se unificó el tópico en `solicitudes-reserva`, se configuró la importación del realm `reservas-realm` y se agregaron callbacks HTTP/HTTPS. Restore y build correctos; Application 69/69 y BookingAPI 29/29; Infrastructure 1/24 correctos y 23 omitidos por falta de MongoDB. Docker E2E no verificable porque la CLI no está instalada.

### Texto original

> Continuá trabajando sobre la rama actual `develop`. No cambies a `Feature-1---Availability-Site`. Preservá los cambios locales, verificá la divergencia con `origin/develop`, resolvé exclusivamente los problemas 1.2 y 1.6 y no trabajes en el problema 1.5. No hagas commits ni push.

## P-0013

- Registrado: 2026-11-05T22:08:58-03:00 (America/Argentina/Buenos_Aires)
- Categoría: Git y publicación
- Estado: bloqueado
- Archivos afectados: `AvailabilitySite.Web/appsettings.json`, `docker-keycloak/docker-compose.yml`, `docker-keycloak/realm-export.json`
- Resultado: el remoto `origin/develop` avanzó 14 commits mientras la rama local conserva 1 commit propio. No se creó commit ni se ejecutó push; es necesario integrar primero el remoto.

### Texto original

> Autorizo realizar el commit y push de las correcciones de los problemas 1.2 y 1.6 en `develop`, incluyendo únicamente los archivos funcionales autorizados y el registro de prompts. Detenerse si el push requiere integrar cambios remotos.

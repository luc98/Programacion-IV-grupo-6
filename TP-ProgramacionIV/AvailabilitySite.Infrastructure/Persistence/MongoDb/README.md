# Persistencia de Availability con MongoDB Atlas

Implementación de la tarea *"Implementación de persistencia NoSQL con MongoDB Atlas"*.
Todo vive en `AvailabilitySite.Infrastructure`. Domain y Application no se modificaron.

## 1. Configuración de MongoDB Atlas

1. En Atlas, crear un proyecto y un cluster. Cualquier cluster de Atlas (incluido el M0 gratuito) es un
   replica set, así que admite transacciones multidocumento.
2. *Database Access*: crear un usuario para la app con rol `readWrite` sobre `AvailabilityDb`.
3. *Network Access*: agregar la IP de cada integrante (o `0.0.0.0/0` solo para el TP).
4. *Connect → Drivers*: copiar la cadena `mongodb+srv://...`.

### Variables de configuración

| Clave                          | Valor por defecto | Dónde va                               |
|--------------------------------|-------------------|----------------------------------------|
| `MongoDB:ConnectionString`     | (obligatoria)     | User Secrets o variable de entorno     |
| `MongoDB:DatabaseName`         | `AvailabilityDb`  | `appsettings.json`                     |
| `MongoDB:Outbox:MaxAttempts`   | `5`               | opcional                               |
| `MongoDB:Outbox:LeaseDuration` | `00:00:30`        | opcional                               |
| `MongoDB:Outbox:RetryBaseDelay`| `00:00:05`        | opcional                               |
| `MongoDB:Outbox:MaxRetryDelay` | `00:05:00`        | opcional                               |

La cadena de conexión **nunca** se guarda en el repositorio:

```bash
# User Secrets (desarrollo), desde TP-ProgramacionIV/
dotnet user-secrets set "MongoDB:ConnectionString" "mongodb+srv://usuario:clave@cluster.mongodb.net/" --project AvailabilitySite.Web

# o variable de entorno (cualquier ambiente)
export MongoDB__ConnectionString="mongodb+srv://..."          # macOS / Linux
setx MongoDB__ConnectionString "mongodb+srv://..."            # Windows
```

Si falta la cadena, la app no arranca y muestra `MongoDB:ConnectionString es obligatoria`.

## 2. Colecciones y documentos

Base: `AvailabilityDb`. Los `Guid` se guardan como UUID estándar y las fechas en UTC.

**Events**: `_id` (EventId), `Name`, `Venue`, `StartsAt`.

**Seats**: `_id` (SeatId), `EventId`, `Label`, `IsAvailable`.
`IsAvailable` es la disponibilidad informativa que muestra el sitio. Availability no confirma reservas
ni administra el estado definitivo de los asientos vendidos: eso es de Booking.

**ReservationRequests**: `_id` (RequestId, el mismo que viaja a Booking como `SolicitudId`), `EventId`,
`SeatId`, `UserId`, `IdempotencyKey`, `RequestStatus` (texto, hoy siempre `Pending`), `CreatedAt`.
No hay una máquina de estados propia: el estado de la reserva lo informa Booking.

**OutboxMessages**: `_id` (MessageId), `RequestId`, `MessageType` (`ReservationRequested`), `Payload`,
`CreatedAt`, `PublishedAt`, `Status` (`Pending` / `Processing` / `Published` / `Failed`), `RetryCount`,
`LastError`, más tres campos para reintentos y concurrencia: `NextAttemptAt`, `LockedBy` y `LockedUntil`.

`Payload` es el JSON del mensaje acordado con Booking, sin cambios:

```json
{"SolicitudId":"...","EventoId":"...","AsientoId":"...","UsuarioId":"..."}
```

Ejemplo para cargar datos a mano (mongosh):

```js
use AvailabilityDb
db.Events.insertOne({ _id: UUID("11111111-1111-1111-1111-111111111111"), Name: "Recital", Venue: "Teatro Central", StartsAt: ISODate("2027-03-13T00:00:00Z") })
db.Seats.insertOne({ _id: UUID("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1"), EventId: UUID("11111111-1111-1111-1111-111111111111"), Label: "A-01", IsAvailable: true })
```

## 3. Índices

Se crean al iniciar la app (`MongoIndexInitializerHostedService`). Crearlos es idempotente.

| Colección           | Índice                                   | Para qué                               |
|---------------------|------------------------------------------|----------------------------------------|
| Events              | `_id` (EventId)                          | buscar evento                          |
| Seats               | `ux_seats_event_seat` (EventId, _id) único | asientos de un evento                |
| ReservationRequests | `_id` (RequestId)                        | buscar solicitud                       |
| ReservationRequests | `ux_requests_user_idempotency_key` (UserId, IdempotencyKey) **único** | idempotencia |
| ReservationRequests | `ix_requests_user_created_at` (UserId, CreatedAt desc) | "mis solicitudes"        |
| OutboxMessages      | `_id` (MessageId)                        | buscar mensaje                         |
| OutboxMessages      | `ix_outbox_status_created_at` (Status, CreatedAt) | pendientes por antigüedad     |
| OutboxMessages      | `ix_outbox_status_next_attempt` (Status, NextAttemptAt) | próximo a reclamar      |
| OutboxMessages      | `ux_outbox_request_message_type` (RequestId, MessageType) único | un mensaje por solicitud |

## 4. Idempotencia

- `UserId + IdempotencyKey` identifica una operación. Lo garantiza el **índice único**, no una consulta
  previa: si llegan dos envíos simultáneos, la base acepta uno solo.
- El otro recibe `DuplicateReservationRequestException`, que trae la solicitud original
  (`Existing`, con su `RequestId`).
- `AddOrGetExistingAsync` hace lo mismo sin excepción: devuelve `(Request, Created)`.
- Que la misma clave se use con datos distintos (otro asiento) lo detecta `RequestReservationService`
  (`idempotency_key_conflict`).

## 5. Transacción y Outbox

`MongoReservationRequestRepository.AddAsync` guarda **la solicitud y su mensaje de Outbox en una sola
transacción multidocumento**: quedan los dos o ninguno.

Operaciones para el publisher (`IOutboxRepository`, implementada por `MongoOutboxRepository`):

1. `ClaimBatchAsync(processorId, n)` / `TryClaimNextAsync(processorId)`: reclama mensajes de forma atómica
   (`Pending → Processing`, con `LockedBy` y `LockedUntil`). Dos procesos nunca reclaman el mismo mensaje.
2. Publicar en Service Bus.
3. **Solo cuando el broker confirmó**, `MarkPublishedAsync`. Si falló, `MarkFailedAsync(error)`: suma
   `RetryCount`, guarda `LastError` y programa el reintento con espera exponencial. Al llegar a
   `MaxAttempts` el mensaje queda en `Failed`.
4. `RequeueAsync` vuelve a `Pending` un mensaje `Failed` (recuperación manual).

Casos contemplados:
- **Reinicio o caída del proceso:** los mensajes que quedaron en `Processing` vuelven a reclamarse cuando
  vence su `LockedUntil`.
- **Publicación duplicada:** si se publica y el proceso se cae antes de `MarkPublishedAsync`, el mensaje
  se publica otra vez. El consumidor (Booking) tiene que ser idempotente por `SolicitudId`.
- `GetPendingAsync` lista los pendientes, solo para consulta.

## 6. Errores

Los repositorios no dejan salir excepciones de MongoDB:
- `AvailabilityPersistenceException` lleva un mensaje apto para mostrar, la excepción original como
  `InnerException` (para logs) e `IsTransient`:
  - `true`: conexión caída, timeout o error transitorio de transacción. Corresponde a
    `DependencyUnavailable`.
  - `false`: el resto de los errores.
- `DuplicateReservationRequestException` cubre el conflicto del índice único de idempotencia.
- La cancelación (`OperationCanceledException`) se propaga tal cual.

## 7. Pruebas de integración

Proyecto `AvailabilitySite.Infrastructure.IntegrationTests` (24 pruebas). Corren contra un MongoDB
real, nunca contra mocks. Cada clase usa una base aleatoria `AvailabilityDb_Tests_*` que se borra al
terminar.

```bash
# Opción A: con Docker abierto (levanta mongo:7.0 con replica set automáticamente)
dotnet test TP-ProgramacionIV.slnx

# Opción B: contra un cluster de prueba de Atlas (NO el de desarrollo)
export AVAILABILITY_MONGODB_TEST_CONNECTION_STRING="mongodb+srv://.../"
dotnet test TP-ProgramacionIV.slnx
```

Sin Docker y sin la variable, las pruebas de MongoDB aparecen como omitidas y no fallan.

Cubren:
- **Disponibilidad:** evento existente, inexistente, sin asientos, asientos del evento e índices.
- **Solicitudes:** crear, buscar por id, listar por usuario, repetir la clave, reutilizarla con otros
  datos, envíos concurrentes con la misma clave, base caída y cancelación.
- **Outbox:** transacción con la solicitud, fallo de transacción (rollback), pendientes, reclamo
  concurrente, publicado, errores y reintentos, paso a `Failed` y reintento manual, lease vencido
  después de una caída.
- **Registro en DI:** resuelve a Mongo, crea los índices al arrancar y valida la configuración.

## 8. Para coordinar con el backend principal

Nada de esto cambió contratos. Son propuestas:

1. **Publisher:** `RequestReservationService` todavía llama a `IReservationRequestPublisher.PublishAsync`
   justo después de `AddAsync`. Con el Outbox, ese llamado directo sobra: el worker publica desde
   `OutboxMessages`. Si se dejan los dos, cada solicitud se publica dos veces.
2. **Idempotencia concurrente:** `IReservationRequestRepository.AddAsync` devuelve `Task`. Con dos envíos
   simultáneos, el segundo recibe `DuplicateReservationRequestException`, pero Application no la conoce.
   Propuesta: cambiar el contrato a algo como `AddOrGetExistingAsync` (ya está implementado) o capturar el
   error y volver a llamar a `FindByIdempotencyKeyAsync`.
3. **Evento inexistente:** `IAvailabilityQueryRepository.GetSeatsAsync` no puede distinguir "no existe"
   de "sin asientos". Propuesta: usar `FindSeatsAsync` (`null` = no existe).
4. **Seguimiento y "mis solicitudes":** `FindByIdAsync` y `ListByUserAsync` ya existen en el
   repositorio Mongo, pero no en la interfaz. Lo mismo `ListEventsAsync` y `FindEventAsync` para
   `IListEventsUseCase`.
5. **Errores de persistencia:** Application no captura `AvailabilityPersistenceException`. Propuesta:
   traducir `IsTransient = true` a `DependencyUnavailable`.
6. `InMemoryAvailabilityQueryRepository` (en `Availability/`) quedó sin registrar. Se puede borrar o usar
   en pruebas.

## 9. Archivos creados y modificados

Creados (`AvailabilitySite.Infrastructure`):
- `Outbox/IOutboxRepository.cs`: interfaz, estados y modelos del Outbox.
- `Persistence/MongoDb/MongoDbOptions.cs`: opciones de conexión y del Outbox.
- `Persistence/MongoDb/MongoDbContext.cs`: base y colecciones.
- `Persistence/MongoDb/Documents/AvailabilityDocuments.cs`: documentos de Events, Seats y ReservationRequests.
- `Persistence/MongoDb/Documents/OutboxMessageDocument.cs`
- `Persistence/MongoDb/MongoErrors.cs`: excepciones y traducción de errores.
- `Persistence/MongoDb/MongoIndexInitializer.cs`: índices y hosted service.
- `Persistence/MongoDb/MongoAvailabilityQueryRepository.cs`
- `Persistence/MongoDb/MongoReservationRequestRepository.cs`
- `Persistence/MongoDb/MongoOutboxRepository.cs`
- `Persistence/MongoDb/MongoDbServiceCollectionExtensions.cs`: `AddAvailabilityMongoDb`.
- `Persistence/MongoDb/README.md`: este documento.

Creado: el proyecto `AvailabilitySite.Infrastructure.IntegrationTests`, con su fixture y 4 clases de pruebas.

Modificados:
- `AvailabilitySite.Infrastructure/DependencyInjection.cs`: `AddAvailabilityInfrastructure` registra
  MongoDB en lugar del repositorio en memoria.
- `AvailabilitySite.Infrastructure/AvailabilitySite.Infrastructure.csproj`: suma `MongoDB.Driver` y
  `Microsoft.Extensions.Hosting.Abstractions`.
- `AvailabilitySite.Web/appsettings.json`: `MongoDB:DatabaseName`, sin la cadena de conexión.
- `AvailabilitySite.Web/AvailabilitySite.Web.csproj`: `UserSecretsId`, para `dotnet user-secrets`.
- `TP-ProgramacionIV.slnx`: suma el proyecto de pruebas.

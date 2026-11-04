# Availability Site — Front (Razor MVC)

Pantallas de Availability: el usuario consulta eventos y asientos, inicia sesión, envía una solicitud de reserva y sigue su estado hasta que Booking la confirma o la rechaza.

Responsable del front: Jonathan Mariño.

## Pantallas

| Pantalla | URL | Acceso | Caso de uso |
|---|---|---|---|
| Eventos | `/` | público | `IListEventsUseCase` |
| Disponibilidad de un evento | `/Events/Availability/{eventId}` | público | `IGetEventAvailabilityUseCase` |
| Confirmar solicitud | `/Reservations/Confirm?eventId=&seatId=` | con sesión | `IGetEventAvailabilityUseCase`, `IRequestReservationUseCase` |
| Seguimiento | `/Reservations/Track/{requestId}` | con sesión, solo solicitudes propias | `IListCurrentUserReservationRequestsUseCase`, `IGetReservationRequestStatusUseCase` |
| Mis solicitudes | `/Reservations` | con sesión | `IListCurrentUserReservationRequestsUseCase`, `IGetReservationRequestStatusUseCase` |
| Login / logout | `/Account/Login`, `/Account/Logout` (POST) | — | OIDC con Keycloak |

## Cómo está armado

- **Controllers** (`Controllers/`): no tienen reglas de negocio. Llaman a los casos de uso de Application y traducen el resultado (`Result<T>` / `ApplicationError`) a una pantalla. `AppController` centraliza los errores: sin sesión manda al login; para 404, 403 y servicio caído muestra una pantalla de estado.
- **Modelos de pantalla** (`Models/ViewModels.cs`): envuelven los DTOs de Application sin cambiarlos.
- **Presentación** (`Presentation/`):
  - `EventLookup`: completa el nombre del evento y la etiqueta del asiento, porque los DTOs traen solo ids.
  - `StatusTexts`: traduce los estados de Booking (`Retenida`, `Confirmada`, `Rechazada`, `Vencida`) y los motivos de rechazo a textos.
  - `DateTimeDisplay`: muestra las fechas en hora de Argentina.
- **Registro**: `Composition/AvailabilityServiceRegistration.cs` (`AddAvailabilityPresentation`) registra solo lo de la capa web. Los casos de uso, los repositorios y la autenticación se registran en `Program.cs`.

## Decisiones

- **Solicitud recibida no es reserva confirmada.** Al enviar, se redirige al seguimiento (Post/Redirect/Get) y el texto lo aclara.
- **Idempotencia.** La clave se genera al mostrar el formulario y se reutiliza si hay que reintentar la misma operación. Si no se sabe si la solicitud llegó, no se reenvía sola: el usuario reintenta con la misma clave.
- **Seguimiento con polling** (`wwwroot/js/tracking.js`):
  - Una consulta a la vez.
  - Se detiene en estados finales y al ocultar la pestaña.
  - Ante errores temporales reintenta con espera creciente.
  - El HTML del estado lo arma el servidor (`_TrackingPanel`), así los textos no se duplican en JavaScript.
- **Pendiente mientras Booking no la conoce.** Recién enviada, la solicitud todavía no llegó a Booking (la publica el Outbox), así que se muestra como pendiente y no como inexistente.
- **Solo solicitudes propias.** El seguimiento verifica que la solicitud esté entre las del usuario actual.
- **Seguridad:**
  - Antiforgery en todos los POST.
  - Los formularios se validan en el servidor.
  - El login solo vuelve a URLs locales.
  - Los tokens de Keycloak no se envían al navegador.
- **Doble envío.** `site.js` deshabilita el botón después del primer clic. La protección real es la clave de idempotencia.
- **Accesibilidad básica:** enlace para saltar al contenido, mensajes de estado con `aria-live` y textos ocultos para lectores de pantalla.

## Correrlo

Requisitos: .NET 10 y Keycloak en `http://localhost:8080` con el realm `reservas-realm`. El cliente `availability-site` tiene que aceptar `http://localhost:5215/*` como URL de redirección y de logout.

Secretos (desde `TP-ProgramacionIV`; nunca van al repo):

```
dotnet user-secrets set "MongoDB:ConnectionString" "mongodb+srv://..." --project AvailabilitySite.Web
dotnet user-secrets set "KeycloakService:ClientSecret" "..." --project AvailabilitySite.Web
dotnet user-secrets set "ServiceBus:ConnectionString" "Endpoint=sb://..." --project AvailabilitySite.Web
```

```
dotnet run --project AvailabilitySite.Web --launch-profile http
```

Abrir `http://localhost:5215`. Si no hay eventos cargados en MongoDB, la pantalla de eventos dice "Por ahora no hay eventos publicados". La base se documenta en `AvailabilitySite.Infrastructure/Persistence/MongoDb/README.md`.

Si Booking (`http://localhost:5156`) no está levantado, el seguimiento avisa que no pudo consultar el estado y sigue reintentando solo, y "Mis solicitudes" muestra las solicitudes como pendientes.

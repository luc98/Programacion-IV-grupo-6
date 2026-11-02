# Happy Path y flujo de fallo

Punto 3.j del trabajo práctico: la solución debe compilar y ejecutar un flujo de camino feliz de reserva de asientos y uno de fallo cuando falta un componente.

Este documento dice qué se puede ejecutar hoy con el Compose del repositorio, el guion completo para cuando estén todos los servicios y qué debería pasar cuando falta uno. Cada afirmación indica si se verificó ejecutándola o si sale de leer el código de la rama correspondiente.

## 1. Qué corre hoy

| Componente | En el Compose | Estado |
|---|---|---|
| Keycloak y su base de datos | Sí | Corre. Con el export del realm que hay en el repositorio importa `reservas-realm` y entrega tokens |
| Base de datos de Booking | Sí | Corre y queda saludable. Solo interna: no publica puertos |
| Servicio de notificaciones | Sí | Corre con la fuente de mensajes simulada. Tiene un lector de Service Bus que se activa por configuración. Se probó con el emulador oficial de Service Bus y todavía no contra Azure real |
| Availability | No | No tiene Dockerfile. Todavía no publica en el broker |
| Booking | No | No tiene Dockerfile |
| Payment | No | Sin código |

El Compose publica un solo puerto, el 8080 de Keycloak, y solo en `127.0.0.1`. Ninguna base de datos se publica, por lo que no choca con una base local en el puerto 5432.

## 2. Cómo levantarlo

Desde la raíz del repositorio:

```bash
cp .env.example .env
docker compose up -d --build
```

El archivo `.env` es opcional: sin él se usan contraseñas de desarrollo. El `.gitignore` compartido del grupo ya ignora `.env`, así que ahí van las claves reales y nunca al repositorio. Mientras ese `.gitignore` no esté en la rama, conviene agregar `.env` a la exclusión local del clon (`.git/info/exclude`).

Con el export del realm, que vive en `docker-keycloak/realm-export.json`, en la rama de luc98:

```bash
docker compose -f docker-compose.yml -f docker-compose.realm.yml up -d
```

El archivo se puede cambiar con la variable `KEYCLOAK_REALM_FILE`. El realm solo se importa la primera vez, cuando la base de Keycloak está vacía.

Para apagar todo: `docker compose down`. Con `-v` también se borran los datos.

Para entrar a una base: `docker compose exec booking-db psql -U postgres -d BookingDb`.

## 3. Prueba de humo, lo que se ejecuta hoy

```bash
./scripts/smoke-test.sh
```

Levanta el Compose y verifica:

- que las dos bases de datos queden saludables;
- que Keycloak responda en `http://127.0.0.1:8080`;
- que el notificador procese los cuatro mensajes de ejemplo: 2 avisos enviados, 1 aviso repetido descartado y 1 mensaje inválido en la cola de errores;
- que los registros lleven el `TraceId`.

Salida de la última ejecución, que se verificó ejecutándola:

```text
[00:31:04] Infraestructura
  OK    La base de Keycloak está saludable
  OK    La base de Booking está saludable
  OK    Keycloak responde en http://127.0.0.1:8080
[00:31:45] Servicio de notificaciones
  OK    Procesó los mensajes de ejemplo
  OK    Envió 2 avisos al usuario
  OK    Descartó 1 aviso repetido
  OK    Mandó 1 mensaje inválido a la cola de errores
  OK    Los registros llevan el TraceId

[00:31:46] PRUEBA OK
```

## 4. Happy Path completo

Los pasos son los del flujo del enunciado. La última columna dice qué hace falta para que cada uno funcione dentro del Compose.

| Paso | Qué pasa | Cómo verlo | Qué falta |
|---|---|---|---|
| 1. Consulta | Un visitante ve los asientos de un evento, sin iniciar sesión | En el sitio de Availability, el catálogo de eventos y sus asientos | Dockerfile de Availability |
| 2. Autenticación | El usuario obtiene un token de Keycloak | Inicio de sesión con Google en el navegador. Un servicio lo pide con `client_credentials` (ver abajo) | Una audiencia en los tokens y un usuario de prueba (ver hallazgos) |
| 3. Solicitud | El usuario pide reservar un asiento. Availability registra la solicitud con un `SolicitudId`, la publica en el broker y responde "recibida" | En Availability, `Reservations/Confirm` | Que Availability publique en el broker, y su Dockerfile |
| 4. Procesamiento | Una réplica de Booking toma el mensaje y crea la reserva Retenida si el asiento está libre | Log de Booking con el `SolicitudId` | Dockerfile de Booking y topics acordados |
| 5. Cobro | Booking llama a Payment por gRPC | Log de Booking y de Payment con el mismo `TraceId` | Payment completo |
| 6. Resultado | Booking confirma o rechaza la reserva y publica `ReservaConfirmada` | Log de Booking | Lo mismo que el paso 5 |
| Avisos | El notificador avisa cuando la reserva queda creada y cuando se confirma | Log del notificador | Una suscripción de Service Bus para el notificador, con el lector activado por configuración |
| 7. Seguimiento | El usuario consulta el resultado en Availability, que lo consulta a Booking | En Availability, `Reservations/Track`. Booking responde en `GET api/reservas/solicitudes/{solicitudId}` y exige token | Lo mismo que los pasos 3 y 4 |
| 8. Vencimiento | Una reserva retenida sin resultado pasa a Vencida y libera el asiento | Booking lo hace con un proceso en segundo plano que corre cada 10 segundos por configuración | Dockerfile de Booking |

### Cómo seguir una reserva

Con la plantilla de logs, todos los registros de una reserva llevan el mismo `TraceId`, que nace en Availability con el valor del `SolicitudId`:

```bash
docker compose logs --no-color | grep <TraceId>
```

Si un servicio todavía no adoptó la plantilla, igual se puede seguir buscando el `SolicitudId`, que aparece en los registros de Booking y viaja como `CorrelationId` en los eventos al notificador.

### Pedir un token de servicio

Es la llamada que usa el grupo. Se verificó ejecutándola con el export del repositorio, cuyo realm se llama `reservas-realm`; con el realm que usa luc98 en su equipo, `booking-realm`, es la misma llamada con ese nombre:

```bash
curl -s -X POST http://localhost:8080/realms/booking-realm/protocol/openid-connect/token \
  -d grant_type=client_credentials -d client_id=booking-api -d client_secret=<secreto>
```

## 5. Flujo de fallo: falta un componente

Cada fila dice cómo simular la falta con `docker compose stop` y qué debería pasar.

| Componente que falta | Cómo simularlo | Qué debería pasar | Cómo se sabe |
|---|---|---|---|
| **Payment** | `docker compose stop payment` | Booking no logra cobrar. Cuando se agotan el tiempo límite y los reintentos, rechaza la reserva con el motivo "pago no disponible", en lugar de fallar con un error. El usuario recibe el aviso de reserva creada y no el de confirmada | Leído en `BookingService` de la rama de Booking. Sin ejecutar: Payment no existe |
| **Notificaciones** | `docker compose stop notification-service` | La reserva se completa igual, porque no depende del aviso. Los mensajes esperan en la suscripción del broker hasta que el notificador vuelve | Es el diseño de Service Bus y del servicio. El lector se probó con el emulador oficial de Service Bus. Sin ejecutar contra Azure real: falta una suscripción |
| **Keycloak** | `docker compose stop keycloak` | No se pueden obtener tokens, así que las llamadas a Booking se rechazan y Availability no puede consultar el seguimiento | Diseño. Sin ejecutar: Booking no está en el Compose |
| **Message Broker** | Quitar la cadena de conexión | Availability no puede publicar la solicitud | Sin ejecutar: falta que Availability publique |

El flujo de fallo más directo para mostrar es el de Payment, porque es una falta que Booking maneja a propósito.

## 6. Hallazgos al levantar el Compose

Cosas que se vieron al probar con el realm del grupo y que conviene resolver antes de unir los servicios:

1. **Los tokens no traen audiencia.** Se pidieron tokens de `booking-api`, `notifications-api` y `payment-api`, y ninguno tiene el campo `aud`. Booking exige la audiencia `booking-api`, así que rechazaría todas las llamadas. Hace falta un mapeo de audiencia en los clientes del realm.
2. **El export del repositorio está desactualizado.** El grupo usa el realm `booking-realm`, como confirmó luc98 en el chat y como apunta Booking, pero el archivo `realm-export.json` que se subió es de un realm llamado `reservas-realm`. Hay que volver a exportar el realm real y subirlo, para que el Compose importe el mismo que usan todos.
3. **Los secretos de los clientes son el texto enmascarado.** El archivo exportado enmascara los secretos y Keycloak los importa tal cual, así que el secreto de cada cliente resulta ser esa máscara. Funciona para desarrollo, pero hay que definir secretos reales en el realm de desarrollo.
4. **El puerto 5432.** El Compose de Keycloak y el de la base de Booking lo publican los dos y chocan entre sí, y también con cualquier base local. El Compose general no publica bases.
5. **El inicio de sesión de usuarios es con Google.** Para automatizar el paso 2 con un usuario hace falta uno con contraseña local, porque Google exige un navegador.
6. **Emisor estable.** El Compose fija el nombre público de Keycloak en `http://localhost:8080`, que es el que usa el grupo en sus configuraciones, para que el `iss` de los tokens no dependa de desde dónde se pidan. Se verificó consultando Keycloak por `localhost` y por `127.0.0.1`, desde el equipo anfitrión y no desde otro contenedor.

## 7. Cómo se suman los servicios que faltan

1. Cada servicio necesita un Dockerfile, como el del notificador en `TP-ProgramacionIV/NotificationService/Dockerfile`.
2. Se agrega al `docker-compose.yml` siguiendo el ejemplo del notificador, en la red `reservas`. Los puertos se publican solo en `127.0.0.1`.
3. La configuración llega por variables de entorno, con doble guion bajo para los niveles. Por ejemplo, Booking:
   - `ConnectionStrings__BookingDb=Host=booking-db;Port=5432;Database=BookingDb;Username=postgres;Password=<clave>`
   - `Authentication__Authority=http://localhost:8080/realms/booking-realm`, que es el nombre con el que Keycloak firma los tokens. Desde otro contenedor, `localhost` no llega a Keycloak, así que hace falta separar la dirección de consulta de los metadatos (`http://keycloak:8080`) del emisor esperado
   - `GrpcSettings__PaymentServiceUrl=http://payment:8080`
4. La cadena de conexión de Azure Service Bus va en el archivo `.env`, que no se sube al repositorio. Para Booking se pasa como `AzureServiceBus__ConnectionString`. El notificador ya la recibe desde `.env` con `SERVICEBUS_CONNECTION_STRING`, y se activa con `NOTIFICATION_SOURCE=ServiceBus`.

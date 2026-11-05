# Historial de Prompts - Román Ranauro (Dupla 2)
**Asignatura**: Programación IV - UTN  
**Grupo**: 6  
**Integrante**: Román Ranauro  
**Dupla**: Román Ranauro & Felipe Díaz  
**Microservicio Asignado**: `BookingAPI` (`TP-ProgramacionIV/BookingAPI/`)  
**Rama**: `feature-2-Booking-REST-API`  

---

## Registro de Interacciones y Prompts

### Entrada #1 - Inicialización del Proyecto, Reglas y Análisis Arquitectónico
- **Fecha**: 28/10/2026
- **Prompt Enviado**:
```text
# PROMPT DE INICIALIZACIÓN - TRABAJO PRÁCTICO PROGRAMACIÓN IV (UTN) # ROL: Román Ranauro (Dupla 2 - Booking REST API) | Grupo 6
Hola Antigravity. Vas a actuar como mi Ingeniero de Software Principal y Asistente Experto en .NET 8 / C# para el Trabajo Práctico Integrador de Programación IV (UTN).
---
1. CONTEXTO DEL PROYECTO Y ENTORNO DE TRABAJO
- Repositorio Git: https://github.com/luc98/Programacion-IV-grupo-6.git
- Ruta Local: C:\Users\ROMAN\Desktop\workspace\UTN\PG4 TP\Grupo 6\Programacion-IV-grupo-6
- Rama Actual: feature-2-Booking-REST-API
- Mi Integrante: Román Ranauro (Dupla 2 con Felipe Díaz - Grupo 6)
- Microservicio Asignado: BookingAPI (Ubicado en TP-ProgramacionIV/BookingAPI/)
---
2. REGLAS FUNDAMENTALES Y PROTOCOLO DE TRABAJO (INVIOLABLES)
1. PROTOCOLO DE APROBACIÓN PREVIA (OBLIGATORIO)
2. DELIMITACIÓN DE RESPONSABILIDAD (SOLO MI PARTE)
3. HISTORIAL DE PROMPTS (ENTREGABLE REQUERIDO POR LA CÁTEDRA)
4. DOCUMENTACIÓN Y CÓDIGO (N-Layer, Dominio Anémico, Justificaciones Teóricas, Hooks Dupla 4)
---
3. ARQUITECTURA Y ESTÁNDARES DE LA MATERIA
- Estilo Arquitectónico: Microservicios.
- Patrón de Arquitectura Interna: N-Layer (Presentation, BLL, DAL).
- Dominio Anémico.
- Justificaciones Teóricas // [JUSTIFICACIÓN TEÓRICA - ...]
- Hooks Dupla 4 // [DUPLA 4 - LOG DE AUDITORÍA / TRACEID]: ...
---
4. TAREAS Y ARCHIVOS ESPECÍFICOS A MI CARGO (ROMÁN RANAURO)
1. Cliente gRPC + Resiliencia con Polly (payment.proto, PaymentGrpcGateway.cs)
2. Integración con Azure Service Bus (ServiceBusListener.cs, ServiceBusEventPublisher.cs)
3. Proceso en Segundo Plano para Expiración TTL (ExpirationWorker.cs)
4. Configuración de Seguridad y Inyección de Dependencias (Program.cs)
---
5. PRIMER PASO A EJECUTAR
Analizar estructura actual, rama feature-2-Booking-REST-API, presentar plan detallado para punto 1 y solicitar aprobación.
```
- **Resumen de lo Realizado**:
  1. Se confirmaron y aceptaron formalmente las reglas de trabajo, restricciones de rol y estándares de la cátedra.
  2. Se realizó el checkout local a la rama de trabajo `feature-2-Booking-REST-API`.
  3. Se inspeccionó la Base Común desarrollada por Felipe:
     - `DAL/Entities/Reserva.cs`, `DAL/Entities/Asiento.cs`, `DAL/Entities/Evento.cs` (diseño de dominio anémico confirmado).
     - `DAL/Enums/EstadoReserva.cs` (`Retenida = 1`, `Confirmada = 2`, `Rechazada = 3`, `Vencida = 4`).
     - `DAL/Enums/MotivoRechazo.cs` (`Ninguno = 0`, `AsientoOcupado = 1`, `PagoFallido = 2`, `PagoNoDisponible = 3`).
     - `BookingAPI.csproj`: paquetes NuGet ya preconfigurados (`Grpc.Net.Client`, `Grpc.Tools`, `Google.Protobuf`, `Polly`, `Azure.Messaging.ServiceBus`, `JwtBearer`).
  4. Se elaboró y estructuró el plan de trabajo detallado para el Punto 1 (`payment.proto` y `PaymentGrpcGateway.cs`) con resiliencia Polly y degradación elegante ante indisponibilidad de pagos.


### Entrada #2 - Implementación Punto 1: Cliente gRPC y Resiliencia con Polly
- **Fecha**: 30/10/2026
- **Prompt Enviado**:
```text
Che Antigravity, feli ya subio los cambios de su parte a feature-2-Booking-REST-API con el DbContext, el repositorio y el BookingService con los dtos base. Hagamos un pull para estar al dia y arranquemos con mi parte de gRPC.

Necesito armar el proto para payments (payment.proto) con el servicio PaymentService y el metodo ProcesarPago pasando el traceId para la dupla 4. Despues en BookingAPI armate el gateway gRPC (PaymentGrpcGateway) implementando la interfaz IPaymentGateway que dejo Feli.

Asegurate de meterle resiliencia con Polly: un timeout de 3 segundos y un par de reintentos con backoff exponencial por si hay microcortes. Y clave lo del punto 3.j de degradacion elegante: si payment se cae o tira timeout, atrapamos la excepcion y devolvemos ServicioCaido = true para que la reserva pase a rechazada con motivo PagoNoDisponible en vez de tirar un 500.

Registrame el cliente gRPC y el gateway en el Program.cs y fijate que compile todo sin errores.
```
- **Resumen de lo Realizado**:
  1. Se verificó el repositorio Git contra `origin/feature-2-Booking-REST-API` integrando los commits de Felipe.
  2. Se creó el contrato gRPC `TP-ProgramacionIV/BookingAPI/BLL/Protos/payment.proto` definiendo el servicio `PaymentService` y los mensajes tipados `ProcesarPagoRequest` (con `trace_id`) y `ProcesarPagoResponse`.
  3. Se actualizó `BookingAPI.csproj` incluyendo el `<Protobuf>` como cliente gRPC (`GrpcServices="Client"`), compilando con éxito y generando los stubs con `Grpc.Tools`.
  4. Se implementó `TP-ProgramacionIV/BookingAPI/BLL/Gateways/PaymentGrpcGateway.cs` cumpliendo con la interfaz `IPaymentGateway`:
     - **Resiliencia con Polly**: composición `AsyncPolicyWrap` con `TimeoutAsync` (3s) y `WaitAndRetryAsync` (2 reintentos con backoff exponencial) ante excepciones transitorias `RpcException`.
     - **Degradación Elegante (Punto 3.j del TP)**: captura de `TimeoutRejectedException`, `RpcException` o fallos de red devolviendo `ServicioCaido = true`, lo que permite que `BookingService` pase la reserva a estado `Rechazada` con motivo `PagoNoDisponible` de forma controlada y sin arrojar errores no manejados al cliente.
     - **Auditoría para Dupla 4**: logs estructurados propagando `traceId` en metadatos gRPC (`x-trace-id`) y en consola.
     - **Justificaciones teóricas**: comentarios explícitos `// [JUSTIFICACIÓN TEÓRICA - ...]` documentando el patrón Gateway, resiliencia y degradación elegante.
  5. Se configuró `appsettings.json` con la sección `"GrpcSettings:PaymentServiceUrl"`.
  6. Se registró en `Program.cs` la inyección de dependencias para `GrpcChannel`, `PaymentService.PaymentServiceClient` y `IPaymentGateway -> PaymentGrpcGateway`.
  7. Se ejecutó `dotnet build` obteniendo 0 errores y 0 advertencias.


### Entrada #3 - Implementación Punto 2: Integración con Azure Service Bus (Pub/Sub)
- **Fecha**: 30/10/2026
- **Prompt Enviado**:
```text
Joya, compilo de diez. Ahora pasemos a la parte de Azure Service Bus para el pub/sub del TP (puntos 3.g y 3.h).

Por un lado necesito el publisher (ServiceBusEventPublisher) que implemente IEventPublisher para mandar los eventos de ReservaCreada (cuando queda retenida) y ReservaConfirmada (despues del pago) al topic de notificaciones, asi el Notification Service de los chicos los consume. Metamosle un DTO propio para el evento de integracion asi no atamos las entidades de la base al broker, y pasale el traceId y tipo de evento en las application properties.

Y por otro lado armate el listener (ServiceBusListener) con un BackgroundService para suscribirse a las solicitudes que manda Availability. Ojo aca con la inyeccion de dependencias: como el listener es singleton y el BookingService con el DbContext son scoped, acordate de inyectar IServiceScopeFactory y crear un scope por mensaje. Si el json viene roto mandalo a la dead letter queue (DLQ) y si falla algo transitorio liberalo para que reintente.

Dejalo listo en el Program y configura el appsettings.
```
- **Resumen de lo Realizado**:
  1. Se creó el DTO de integración `TP-ProgramacionIV/BookingAPI/BLL/DTOs/ReservaEventoPublicadoDto.cs` para el desacoplamiento de las notificaciones enviadas a Notification Service (Punto 3.h del TP).
  2. Se implementó `TP-ProgramacionIV/BookingAPI/BLL/Gateways/ServiceBusEventPublisher.cs` (implementando `IEventPublisher`):
     - Publicación de eventos `ReservaCreada` (cuando la reserva pasa a `Retenida`) y `ReservaConfirmada` (tras pago exitoso).
     - Serialización de payload con metadatos tipados (`TipoEvento`, `TraceId`, `SolicitudId`) para trazabilidad distribuida (Dupla 4).
     - Justificaciones teóricas de Arquitectura Orientada a Eventos (EDA) y Consistencia Eventual (Punto 3.g del TP).
     - Manejo resiliente y seguro en entornos locales sin cadenas de Azure configuradas.
  3. Se implementó `TP-ProgramacionIV/BookingAPI/Presentation/Listeners/ServiceBusListener.cs`:
     - Hereda de `BackgroundService` para suscripción asincrónica (SUBSCRIBE) de solicitudes generadas por `Availability Site`.
     - Inyección de `IServiceScopeFactory` para instanciación limpia de `IBookingService` en cada mensaje.
     - Manejo de confirmación (`CompleteMessageAsync`), abandono para reintento (`AbandonMessageAsync`) y desvío a Dead Letter Queue (`DeadLetterMessageAsync`) ante JSONs corruptos.
     - Ganchos y logs estructurados con `TraceId` y `CorrelationId` para auditoría (Dupla 4).
  4. Se configuró `appsettings.json` con la sección `AzureServiceBus` (`ConnectionString`, `SolicitudesQueue`, `NotificacionesTopic`).
  5. Se registraron `IEventPublisher` y el worker `ServiceBusListener` en `Program.cs`.
  6. Se compiló el proyecto con `dotnet build` obteniendo 0 advertencias y 0 errores.


### Entrada #4 - User Secrets de Keycloak, Soporte Dinámico de Broker y Expiration Worker (Puntos 3 y 4)
- **Fecha**: 30/10/2026
- **Prompt Enviado**:
```text
Ahi feli me paso por wpp el ClientSecret de keycloak para la autenticacion entre microservicios del punto 3.e. Para no meter credenciales en el repo ni en el appsettings, cargalo directo en los User Secrets de .NET del proyecto.

Despues me hablo Tade de Availability para coordinar pruebas con el service bus. Para no tener dramas si usan colas o topicos, toquemos el ServiceBusListener para que soporte dinamicamente tanto colas directas como topico con suscripcion segun lo que este configurado en el appsettings.

Y por ultimo nos queda el worker de expiracion por TTL (ExpirationWorker.cs). Armate un BackgroundService que corra cada 10 segundos mas o menos, busque con el metodo del repo las reservas retenidas que ya vencieron y las pase a Vencida. Con eso ya queda liberado el asiento solo por la logica de disponibilidad del repositorio, cumpliendo con la consistencia eventual.

Registra el worker en Program.cs y hace un build para confirmar que no rompa nada.
```
- **Resumen de lo Realizado**:
  1. Se configuró el almacén local de secretos de usuario de .NET (`User Secrets`) en `%APPDATA%\Microsoft\UserSecrets\9cd132a1-3bf5-44c3-b133-85c639d2439b\secrets.json` con la clave `Authentication:ClientSecret` provista por Felipe para Keycloak, verificando su correcta resolución con `dotnet user-secrets list` fuera del control de versiones.
  2. Se adaptó `Presentation/Listeners/ServiceBusListener.cs` para brindar soporte dual: detección y suscripción transparente tanto a **Tópicos con Suscripciones** (`SolicitudesTopic` + `SolicitudesSubscription`) como a **Colas** (`SolicitudesQueue`), permitiendo interoperabilidad inmediata con la infraestructura provista por Availability Site.
  3. Se implementó `BLL/Jobs/ExpirationWorker.cs` (Punto 3 del TP):
      - `BackgroundService` periódico que busca reservas en estado `Retenida` cuya `FechaVencimientoUtc <= DateTime.UtcNow` mediante el repositorio.
      - Transiciona automáticamente el estado de las reservas expiradas a `EstadoReserva.Vencida`.
      - **Liberación de asientos**: Debido a la regla de dominio de disponibilidad, pasar la reserva a `Vencida` libera automáticamente el asiento para nuevas solicitudes, alcanzando consistencia eventual de manera autónoma (Puntos 7, 8 y 3.g del TP).
      - Logs de auditoría estructurados con `TraceId` para la Dupla 4.
      - Justificaciones teóricas explícitas documentadas en comentarios de código.
  4. Se actualizó `appsettings.json` con las claves de `AzureServiceBus` y `ExpirationWorker:IntervalSeconds`.
  5. Se completó el registro en `Program.cs` inyectando `ExpirationWorker` como Hosted Service (`AddHostedService<ExpirationWorker>()`).
  6. Se compiló el microservicio completo con `dotnet build`: **0 advertencias y 0 errores**.


### Entrada #5 - Configuración de Entorno en Nueva PC y Sincronización de Rama
- **Fecha**: 04/11/2026
- **Prompt Enviado**:
```text
Buenas Antigravity, me tuve que pasar a otra compu para seguir con el laburo del feature. Necesito que me dejes el entorno listo acá: instalate Git y los SDKs de .NET 8 y 10 en las variables de entorno del usuario así no pide permisos de admin. 

Una vez que tengas eso, postulate en mi rama feature-2-Booking-REST-API, clonate el repo y tirate un pull para estar al día con lo último. Después pegale un build a BookingAPI para confirmar que no rompa nada.
```
- **Resumen de lo Realizado**:
  1. Se instaló `Git` (`Git.MinGit`) en el entorno de usuario y se añadieron sus binarios de manera permanente a la variable de entorno `PATH`.
  2. Se instalaron los SDKs de .NET (8.0 y 10.0 para compatibilidad plena con el target del `.csproj`) mediante el script oficial de instalación de Microsoft, agregándolo permanentemente al `PATH` del usuario.
  3. Se clonó exitosamente el repositorio oficial `https://github.com/luc98/Programacion-IV-grupo-6.git` con la rama `feature-2-Booking-REST-API` en la ruta `C:\Users\Roman\OneDrive - Enta Consulting\Escritorio\workspace\utn\Programacion-IV-grupo-6`.
  4. Se ejecutó `git pull origin feature-2-Booking-REST-API` verificando que la rama local se encuentra 100% al día con el origen remoto.
  5. Se ejecutó la restauración de dependencias y compilación de `BookingAPI.csproj` con `dotnet build`, comprobando **0 errores y 0 advertencias**.


### Entrada #6 - Integración de Contrato gRPC Definitivo, Resiliencia Polly v8, Keycloak y Pruebas
- **Fecha**: 04/11/2026
- **Prompt Enviado**:
```text
Che, estuve chusmeando las otras ramas del repo y los chicos de las otras duplas ya tienen bastante avanzado lo suyo. Vayamos adaptando mi parte de Booking para que encaje de una cuando toque integrar todo:

Primero actualicemos el proto de payments al que armó Alfre en Protos/payment/v1/payment.proto con el namespace compartido. Adaptate el PaymentGrpcGateway para que mande el monto en centavos con int64 y el x-trace-id en los metadatos gRPC, y metele el pipeline de Polly v8 con timeout y circuit breaker para la degradación elegante del punto 3.j.

Después sumale el KeycloakTokenProvider para sacar el token entre microservicios y corregí el appsettings con el realm reservas-realm y el puerto de payment en http://localhost:5260.

Traete también los tests de BookingAPI.Tests que armaron para el gateway y el token, y clavale un dotnet test a ver si pasan todos. Y de paso armame en limpio la info de los contratos y endpoints que le tengo que pasar a cada dupla por WhatsApp para coordinar la conexión.
```
- **Resumen de lo Realizado**:
  1. **Sincronización de Contrato gRPC**: Se incorporó el contrato gRPC único y compartido `TP-ProgramacionIV/Protos/payment/v1/payment.proto` (con `package payment.v1`, namespace `Grupo6.Payment.Contracts.V1`, `int64 monto_centavos`, y `x-trace-id` en metadatos gRPC). Se actualizó `BookingAPI.csproj` con el enlace tipado al proto compartido y se removió la copia preliminar interna.
  2. **Pipeline de Resiliencia Polly v8 y Token Provider**: Se adaptó `PaymentGrpcGateway.cs` implementando `ResilienciaPago.cs` (Polly v8 con Timeout global de 10s, 2 reintentos con backoff exponencial y jitter, Circuit Breaker y timeout por intento de 2s) y `KeycloakTokenProvider.cs` para propagar el Bearer token entre microservicios mediante Client Credentials.
  3. **Ajuste de Configuración en `appsettings.json`**: Se actualizó el Realm de Keycloak a `reservas-realm`, la URL del servicio de pagos a `http://localhost:5260`, y se incorporaron los parámetros de configuración de resiliencia (`ResilienciaPagoOptions`).
  4. **Pruebas y Verificación Automatizada**: Se incorporó el conjunto de pruebas unitarias y de integración `BookingAPI.Tests`. Se ejecutó `dotnet test` ejecutando y superando con éxito **29 pruebas de 29 (0 errores)**, validando el comportamiento ante cobro aprobado, fondos insuficientes, caídas de red, timeouts, circuito abierto y degradación elegante a `PagoNoDisponible`.
  5. Compilación del microservicio `BookingAPI.csproj` con `dotnet build`: **0 errores y 0 advertencias**.


### Entrada #7 - Configuración de Credenciales de Keycloak y Azure Service Bus en User Secrets
- **Fecha**: 04/11/2026
- **Prompt Enviado**:
```text
Ahi me pasó Feli por WhatsApp las credenciales que faltaban para conectar todo en serio: el ClientSecret de keycloak y la connection string de Azure Service Bus con el topic del grupo.

Cargalos directo en los User Secrets de .NET de BookingAPI así no quemamos credenciales en el repo ni en el appsettings, y ya nos queda el proyecto autenticando contra Keycloak y conectado al broker real de Azure.
```
- **Resumen de lo Realizado**:
  1. Se configuraron de forma segura los valores de `Authentication:ClientSecret` y `AzureServiceBus:ConnectionString` en el almacén local de secretos de usuario de .NET (`User Secrets`) del proyecto `BookingAPI` mediante el comando `dotnet user-secrets set`.
  2. Se ejecutó `dotnet user-secrets list` validando la persistencia correcta de ambos secretos en la máquina local fuera del control de versiones de Git, preservando los estándares de seguridad y conectando el microservicio con Keycloak y Azure Service Bus (Puntos 3.e y 3.h del TP).

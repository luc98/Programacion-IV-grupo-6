HISTORIAL DE PROMPTS — AVAILABILITY SITE - Tadeo Céspedes
Trabajo Práctico de Programación IV — Grupo 6
======================================================================
P-01 — INSPECCIÓN INICIAL DEL REPOSITORIO Y LA SOLUCIÓN
Categoría: Relevamiento técnico
Estado registrado: Completado

Revisá el repositorio y la solución antes de proponer cambios. Necesito conocer la estructura de los proyectos, las capas de Availability, las referencias entre proyectos, la versión de .NET, los paquetes utilizados y el estado actual de Git.

Prestá especial atención a la separación entre Web, Application, Domain e Infrastructure. Identificá qué funcionalidades ya están implementadas, cuáles son temporales y cuáles dependen de decisiones de otros integrantes.

Por ahora no modifiques código. Devolvé un diagnóstico con los riesgos principales y los próximos pasos recomendados.

======================================================================
P-02 — ANÁLISIS DEL ENUNCIADO Y ALCANCE DE AVAILABILITY
Categoría: Análisis de requisitos
Estado registrado: Completado

Analizá el enunciado oficial del trabajo práctico y extraé los requisitos que corresponden a Availability Site.

Separá las responsabilidades propias de Availability de las que pertenecen a Booking, Payment, Notification y Authorization Server. Identificá los flujos de consulta de eventos, disponibilidad de asientos, solicitud de reserva y seguimiento del resultado.

No supongas contratos que el enunciado no defina. Dejá identificados los puntos que necesitan acuerdo con los demás integrantes, especialmente nombres de topics, formatos de mensajes, endpoints y mecanismos de autenticación.

No implementes nada en esta etapa. Necesito una base de requisitos para orientar el desarrollo.

======================================================================
P-03 — AVANCE INTERNO SIN DEFINICIONES DE INTEGRACIÓN
Categoría: Diseño y planificación
Estado registrado: Completado

Podemos continuar con la parte interna de Availability aunque todavía no tengamos confirmados todos los detalles de Booking y los demás servicios.

Mantené las dependencias externas detrás de interfaces de Application. No inventes endpoints, nombres de colas, topics ni contratos de mensajes. Documentá qué decisiones quedan pendientes y avanzá únicamente con aquello que no dependa de esas definiciones.

======================================================================
P-04 — CASO DE USO DE CONSULTA DE DISPONIBILIDAD
Categoría: Application
Estado registrado: Completado

Implementá el caso de uso para consultar la disponibilidad de asientos de un evento, respetando la estructura actual de Clean Architecture.

Antes de modificar, revisá las entidades y contratos existentes. La lógica de aplicación debe depender de una abstracción de consulta y no de una implementación concreta de persistencia.

Contemplá el identificador del evento, la consulta de sus asientos y los resultados de error que correspondan. No agregues todavía integraciones externas ni lógica de reserva definitiva.

Al terminar, indicá los archivos modificados y ejecutá una compilación para comprobar que la solución siga consistente.

======================================================================
P-05 — REPOSITORIO TEMPORAL DE DISPONIBILIDAD
Categoría: Infrastructure y Dependency Injection
Estado registrado: Completado

Necesito una implementación temporal de IAvailabilityQueryRepository dentro de Infrastructure para poder probar el caso de uso sin conectar todavía la persistencia definitiva.

Utilizá datos de prueba controlados, reutilizá las entidades existentes y asegurate de devolver solamente los asientos del evento solicitado. Registrá la implementación en Dependency Injection.

No agregues Entity Framework, migraciones ni tablas. Tampoco simules el comportamiento de Booking, Azure Service Bus o la confirmación de reservas. El objetivo es poder desarrollar y probar la consulta mientras se termina de definir la infraestructura.

Al finalizar, compilá la solución y explicá qué habría que reemplazar cuando se conecte la persistencia real.

======================================================================
P-06 — PROYECTO DE PRUEBAS UNITARIAS DE APPLICATION
Categoría: Testing
Estado registrado: Completado

Creá un proyecto separado AvailabilitySite.Application.Tests utilizando xUnit. Debe referenciar Application y Domain, sin depender de Infrastructure.

Agregá pruebas unitarias para GetEventAvailabilityService con estos escenarios:

- Evento válido que contiene asientos.
- Evento válido sin asientos.
- Identificador Guid.Empty.
- Cancelación mediante CancellationToken.
- Error devuelto por el repositorio.

Usá un repositorio falso para aislar el caso de uso. No modifiques el comportamiento productivo para facilitar las pruebas. Ejecutá la suite y reportá el resultado de cada escenario.

======================================================================
P-07 — CONTRATOS Y DTOs DE APPLICATION
Categoría: Diseño de contratos
Estado registrado: Completado

Continuá con los contratos necesarios para los casos de uso de Availability. Antes de crear tipos nuevos, revisá si ya existe una definición equivalente.

Mantené el estado inicial de una solicitud como Pending y obtené el identificador del usuario desde el contexto de identidad autenticado. La idempotencia debe apoyarse en una IdempotencyKey de tipo string.

El comprobante de recepción debe incluir RequestId, EventId, SeatId, ReceivedAt, IsReplay y el estado inicial. Booking será la fuente de verdad del estado definitivo de la reserva.

Utilizá Result<T> para representar resultados y errores de aplicación, distinguiendo validación, recurso inexistente, falta de autenticación, falta de autorización, dependencia no disponible y resultado desconocido.

No agregues contratos de integración que todavía no hayan sido acordados con los otros servicios. Actualizá las pruebas y compilá.

======================================================================
P-08 — PERSISTENCIA MONGODB Y REPOSITORIOS
Categoría: Infrastructure / Persistencia
Estado registrado: Implementación documentada posteriormente

Revisá la infraestructura de persistencia de Availability y prepará los repositorios necesarios para almacenar y consultar eventos, asientos y solicitudes de reserva.

La implementación debe respetar las interfaces definidas en Application. Mantené las dependencias de MongoDB dentro de Infrastructure y evitá que los casos de uso conozcan detalles del driver o de las colecciones.

Revisá también los índices necesarios para las consultas y para la idempotencia de las solicitudes. La configuración de conexión debe obtenerse desde configuración externa, no quedar fijada en el código.

No incluyas cadenas de conexión ni credenciales reales en archivos versionados. Ejecutá las pruebas que puedan realizarse sin una instancia real y dejá identificadas las verificaciones que requieren MongoDB.

======================================================================
P-09 — SOLICITUD DE RESERVA E IDEMPOTENCIA
Categoría: Application / Reglas de aplicación
Estado registrado: Implementación documentada posteriormente

Implementá el flujo de solicitud de reserva en Application utilizando las abstracciones existentes.

El caso de uso debe obtener el usuario autenticado desde ICurrentUserContext, validar los identificadores recibidos y comprobar que el asiento corresponda al evento solicitado.

Incorporá idempotencia por usuario e IdempotencyKey. Si se repite una solicitud con la misma clave y los mismos datos, devolvé el comprobante existente. Si se reutiliza la clave con datos diferentes, devolvé un conflicto.

La solicitud debe registrarse inicialmente como Pending. No confirmes la reserva desde Availability: el resultado definitivo lo determina Booking.

Revisá las pruebas existentes y agregá escenarios para validaciones, usuario no autenticado, idempotencia y asiento inexistente o perteneciente a otro evento.

======================================================================
P-10 — TRANSACTIONAL OUTBOX
Categoría: Persistencia / Consistencia
Estado registrado: Implementación documentada posteriormente

Implementá el patrón Transactional Outbox para garantizar que la persistencia de la solicitud y la generación del mensaje que debe publicarse sean atómicas dentro de MongoDB.

La creación de ReservationRequests y OutboxMessages debe realizarse en la misma transacción. No publiques directamente desde el caso de uso ni dependas de que Azure Service Bus esté disponible durante la operación HTTP.

El mensaje debe quedar pendiente para que un proceso independiente lo publique. Revisá los índices y las restricciones de persistencia para evitar inconsistencias.

Documentá qué garantías ofrece esta solución y qué aspectos siguen dependiendo de la idempotencia del consumidor.

======================================================================
P-11 — OUTBOX BACKGROUND WORKER
Categoría: Procesamiento asíncrono
Estado registrado: Implementación documentada posteriormente

Implementá el Background Worker responsable de procesar los mensajes pendientes del Outbox.

El worker debe reclamar mensajes mediante un mecanismo de lease, evitar que varias instancias procesen simultáneamente el mismo registro durante el período de exclusión y permitir la renovación del lease cuando corresponda.

Después de publicar correctamente, debe marcar el mensaje como publicado. Si ocurre un error, debe aplicar la política de reintentos y conservar información suficiente para volver a procesarlo.

Tené presente que la entrega es al menos una vez: un mensaje puede publicarse más de una vez. No presentes el Outbox como una garantía de exactly-once. La deduplicación definitiva debe contemplarse también en Booking mediante el identificador de solicitud.

Agregá pruebas para los escenarios de éxito, error, lease y reintentos. No elimines ni limpies datos locales para ejecutar las pruebas.

======================================================================
P-12 — PUBLICACIÓN EN AZURE SERVICE BUS
Categoría: Mensajería
Estado registrado: Implementación documentada posteriormente

Conectá el publicador de Availability con Azure Service Bus manteniendo la abstracción IReservationRequestPublisher en Application y la implementación concreta en Infrastructure.

Revisá el contrato de ReservationRequestedMessage y verificá que coincida con el formato que espera Booking. Utilizá el identificador de solicitud como identificador del mensaje para facilitar la deduplicación.

La conexión y el nombre del topic deben obtenerse de configuración externa. No incluyas connection strings ni secretos en appsettings versionados.

Antes de cambiar el nombre de un topic o una subscription, verificá el acuerdo vigente con el equipo de Booking. Si hay diferencias entre configuraciones, documentalas en lugar de elegir unilateralmente.

======================================================================
P-13 — CLIENTE HTTP PARA CONSULTAR BOOKING
Categoría: Integración REST
Estado registrado: Implementación documentada posteriormente

Implementá el cliente HTTP de Availability para consultar el estado de una solicitud en Booking REST API.

El cliente debe respetar IReservationStatusClient y mapear la respuesta de Booking a los contratos de Application. Revisá los códigos HTTP y diferenciá una solicitud todavía no encontrada de una dependencia no disponible o de un error de autenticación.

No marques una reserva como confirmada desde Availability. La respuesta de Booking es la autoridad del estado definitivo.

Mantené la URL base y las opciones de conexión en configuración externa. Agregá pruebas del cliente utilizando HttpClient simulado, incluyendo respuestas exitosas, 404, errores de servidor y cancelación.

======================================================================
P-14 — AUTENTICACIÓN DE USUARIOS CON KEYCLOAK Y OIDC
Categoría: Seguridad / OIDC
Estado registrado: Completado según auditoría

Configurá la autenticación de usuarios de Availability mediante Keycloak utilizando OpenID Connect y Authorization Code Flow con PKCE.

La aplicación MVC debe utilizar una sesión basada en cookies y redirigir al proveedor OIDC cuando un usuario intente acceder a una operación protegida. Desactivá el mapeo automático de claims si es necesario para conservar los nombres originales y obtené la identidad del usuario mediante el claim sub.

Configurá los scopes necesarios y los endpoints de callback. La autoridad debe apuntar al realm acordado por el equipo.

No implementes autenticación local ni almacenes contraseñas de usuarios en Availability. No agregues client secrets al cliente público del frontend. Dejá documentados los redirect URIs que deben configurarse en Keycloak.

Ejecutá build y pruebas disponibles. Aclarà que la autenticación real requiere que Keycloak esté levantado y correctamente configurado.

======================================================================
P-15 — CONTEXTO DEL USUARIO AUTENTICADO
Categoría: Seguridad / Application
Estado registrado: Completado según auditoría

Implementá la adaptación entre la identidad autenticada de ASP.NET Core y el contrato ICurrentUserContext de Application.

La capa Application no debe depender de HttpContext ni de clases de ASP.NET Core. La implementación concreta debe quedar en Web o en la capa de composición correspondiente.

El identificador del usuario debe obtenerse del claim sub de la sesión autenticada. Si no hay identidad válida, el caso de uso debe recibir un contexto no autenticado y responder según el contrato de aplicación.

Agregá pruebas para usuario autenticado y no autenticado. No aceptes el identificador del usuario como dato confiable enviado por el formulario.

======================================================================
P-16 — AUTENTICACIÓN DE SERVICIO A SERVICIO CON CLIENT CREDENTIALS
Categoría: Seguridad M2M
Estado registrado: Completado; commit registrado en Feature1-Availability-back

Necesito separar la autenticación de los usuarios de la autenticación utilizada por Availability para consultar Booking.

Implementá un proveedor de tokens OAuth2 Client Credentials para el cliente de servicio availability-api. El proveedor debe solicitar el token al realm reservas-realm de Keycloak y entregarlo al cliente HTTP de Booking para incorporarlo en el header Authorization: Bearer.

El client secret debe obtenerse desde User Secrets o variables de entorno. No lo escribas en appsettings.json ni lo registres en logs.

Incorporá cache del token, renovación anticipada y control de concurrencia para evitar solicitudes simultáneas innecesarias al endpoint de tokens. Respetá CancellationToken y manejá errores de red o respuestas inválidas sin exponer credenciales.

Reutilizá IAccessTokenProvider si ya existe. Agregá pruebas con un HttpMessageHandler simulado para validar solicitud de token, cache, renovación, concurrencia y fallos.

No modifiques la configuración de Booking sin acuerdo con su responsable. Al finalizar, compilá y ejecutá las pruebas correspondientes. No hagas commit ni push sin autorización.

======================================================================
P-17 — INTEGRACIÓN DEL FRONTEND MVC CON LOS CASOS DE USO REALES
Categoría: Frontend / Integración
Estado registrado: Integrado mediante PR de Feature1-Availability-front

Revisá la integración del frontend MVC con el backend de Availability. El objetivo es que las pantallas dejen de utilizar datos falsos y consuman los casos de uso reales.

Conectá el listado público de eventos, la consulta de disponibilidad, la solicitud de reserva y el seguimiento de solicitudes con sus respectivos casos de uso.

Respetá la división entre controladores, ViewModels y servicios de Application. Las consultas públicas deben seguir disponibles sin iniciar sesión, mientras que las operaciones de reserva y consulta de solicitudes propias deben exigir una identidad autenticada.

El usuario debe provenir de ICurrentUserContext, no de un valor fijo ni de un campo manipulable del formulario. Eliminá únicamente los mocks que efectivamente hayan sido reemplazados y revisá que no queden registros de prueba conectados al contenedor productivo.

No cambies MongoDB, Outbox, los contratos de mensajería ni la integración con Booking salvo que sea indispensable y esté justificado. Ejecutá build y tests e informá qué flujos quedan pendientes de validar con servicios externos.

======================================================================
P-18 — LOGIN Y LOGOUT MEDIANTE KEYCLOAK
Categoría: Frontend / Seguridad
Estado registrado: Integrado mediante PR de frontend

Implementá las acciones de login y logout para el portal MVC utilizando la configuración OIDC existente.

La pantalla de acceso debe ofrecer el ingreso mediante Google a través del Identity Provider configurado en Keycloak. El controlador debe iniciar el challenge OIDC con el parámetro correspondiente para seleccionar el proveedor externo.

El logout debe cerrar la sesión local y la sesión OIDC según corresponda. Validá los returnUrl para impedir redirecciones a sitios externos.

No agregues credenciales de Google al código de Availability. Esas credenciales pertenecen a la configuración de Keycloak. Revisá el comportamiento de los esquemas de autenticación y autorización para evitar que los formularios o las llamadas AJAX reciban redirecciones HTML inesperadas.

Documentá qué parte puede verificarse sin levantar Keycloak y qué parte necesita una prueba real.

======================================================================
P-19 — SEGUIMIENTO DE SOLICITUDES Y POLLING
Categoría: Frontend / Integración REST
Estado registrado: Integrado mediante PR de frontend

Revisá el flujo de seguimiento de solicitudes en el portal MVC.

La pantalla debe listar únicamente las solicitudes del usuario autenticado. Antes de consultar Booking por el estado de una solicitud, verificá que la solicitud pertenezca al usuario actual.

Implementá el endpoint de polling para actualizar el estado sin recargar toda la página. La llamada AJAX debe responder con códigos HTTP adecuados cuando no exista sesión o el usuario no tenga acceso.

El polling debe evitar solicitudes simultáneas, utilizar intervalos razonables y detenerse cuando el estado sea terminal o cuando la sesión ya no sea válida. No muestres una solicitud como confirmada simplemente porque Availability la haya recibido.

Revisá también el comportamiento ante Booking no disponible y ante una solicitud todavía no encontrada.

======================================================================
P-20 — NORMALIZACIÓN DE ESTADOS Y MOTIVOS DE BOOKING
Categoría: Compatibilidad entre servicios
Estado registrado: Completado

Revisá AvailabilitySite.Web/Presentation/StatusTexts.cs y compará los valores que interpreta el frontend con los que devuelve Booking.

Los estados oficiales informados por Booking son Retenida, Confirmada, Rechazada y Vencida. Los motivos incluyen asiento ocupado, pago fallido y pago no disponible.

Asegurate de reconocer los valores en español con espacios, además de los alias en inglés que ya soporte la implementación. Normalizá mayúsculas, minúsculas y espacios externos o internos de manera consistente.

Los estados desconocidos deben tener una presentación segura y no clasificarse accidentalmente como terminales. Los motivos desconocidos no deben convertirse en motivos conocidos.

Agregá pruebas para los valores oficiales, alias, variaciones de mayúsculas y espacios, y entradas desconocidas. Limitá los cambios a la traducción/presentación y sus pruebas; no modifiques BookingService ni los contratos compartidos.

Ejecutá build y tests e informá los resultados reales. No hagas commit ni push.

======================================================================
P-21 — AUDITORÍA POST-MERGE DEL FRONTEND
Categoría: Auditoría técnica
Estado registrado: Completado según informe de auditoría

Realizá una auditoría de la solución después de integrar el frontend en la rama Feature-1---Availability-Site.

Primero revisá el estado de Git y no alteres archivos ni procesos. No ejecutes dotnet clean ni elimines bin, obj, .vs u otros artefactos existentes.

Verificá:
- Restore y build de la solución.
- Tests de Application e IntegrationTests.
- Registro de Dependency Injection.
- Referencias entre proyectos y dirección de dependencias.
- Controladores MVC y rutas principales.
- Autorización de las operaciones protegidas.
- Antiforgery en formularios POST.
- Uso de la identidad autenticada.
- Ausencia de mocks activos en el entorno productivo.
- Configuración de OIDC y Client Credentials.
- Configuración externa requerida para MongoDB, Azure Service Bus y Booking.

No afirmes que una integración externa funciona si no fue probada contra el servicio real. Separá los resultados de compilación, pruebas automatizadas y pruebas E2E. Informá las pruebas omitidas y el motivo.

No resuelvas bloqueos cerrando procesos sin autorización. No hagas commit, push ni merge.

======================================================================
P-22 — VERIFICACIÓN DE BUILD Y TESTS DE LA SOLUCIÓN
Categoría: Validación
Estado registrado: Completado en distintas ejecuciones

Ejecutá las verificaciones de la solución sin limpiar directorios ni finalizar procesos que puedan estar utilizando archivos compilados.

Antes de compilar, revisá si hay procesos de Visual Studio o AvailabilitySite.Web que puedan bloquear las DLL. Si aparece un error de archivo en uso, informalo como bloqueo del entorno y no como error de código.

Cuando sea posible, ejecutá restore, build y tests. Informá por proyecto la cantidad de pruebas aprobadas, fallidas y omitidas.

No ocultes los tests omitidos por falta de MongoDB o Docker. Diferenciá claramente los tests unitarios de las pruebas de integración que requieren infraestructura real.

======================================================================
P-23 — PREPARACIÓN DE CONFIGURACIÓN PARA INTEGRACIÓN COMPLETA
Categoría: Configuración de entorno
Estado registrado: Pendiente de validación E2E

Prepará una lista de configuración necesaria para ejecutar Availability junto con Keycloak, MongoDB, Azure Service Bus y Booking.

Identificá las claves de configuración requeridas y dónde deben definirse. Los secretos deben ir en User Secrets o variables de entorno y no en archivos versionados.

Revisá especialmente:
- MongoDB:ConnectionString.
- ServiceBus:ConnectionString y TopicName.
- Keycloak:Authority y opciones OIDC.
- KeycloakService:ClientId y ClientSecret.
- Booking:BaseUrl.

No inventes valores ni asumas que los servicios están levantados. Indicá cuáles son configuraciones locales, cuáles dependen de Azure y cuáles debe proporcionar el responsable de cada microservicio.

No muestres ni copies secretos en el informe.
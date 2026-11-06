# Historial de prompts — Marcos Gomez

**Dupla**: 4  
**Parte a cargo**: Servicio de Notificaciones (punto 3.h) y logs de auditoría y trazabilidad (3.f)  
**Rama**: `feature4-notification-service`  
**Herramienta**: Claude Code (Claude)  

Los prompts están resumidos y reformulados a partir de mis mensajes reales de la conversación con la herramienta: cada uno junta lo que se pidió en varios mensajes cortos y está completado con los nombres de las clases y las funciones para que se entienda solo. No es la transcripción literal.


## Parte 1: diseño y funcionamiento basico

### Prompt 1: de que se trata

- **Fecha**: 29/10/26
- **Prompt**:

```text
Empecemos con una base del notificador que implemente un Worker Service en C# (.NET 10) para escuchar mensajes de Azure Service Bus. La idea es que le avise al usuario cuando la reserva queda creada y cuando se confirma. Es el punto 3.h del TP de Programación IV: un sistema de reserva de asientos con varios servicios (Availability, Booking, Payment y Keycloak).

Me gustaría poder desarrollar la feature teniendo presente lo que está haciendo el resto, pero sin depender de su codigo. Tiene que ser de bajo acoplamiento para poder cambiar partes sin romper todo: inversión de dependencias, interfaces para las entradas y las salidas (puertos y adaptadores) y cada clase con una sola responsabilidad. De momento solo el esqueleto, sin conexion a Azure: una suerte de codigo "placeholder" para despues ir refinando y completando.

Dame un listado de pasos iniciales y te digo por donde empezar.
```

### Prompt 2: los contratos del nucleo

- **Fecha**: 01/11/26
- **Prompt**:

```text
Vamos con lo basico. Armame tres tipos en una carpeta Core y otra Messaging, sin dependencias externas:
- IncomingEvent (record): MessageId, Subject, CorrelationId, Body, Properties (diccionario de texto) y DeliveryCount (por defecto 1). Es el mensaje crudo, sin interpretar.
- NotificationRequest (record): RecipientId, Type, Data (diccionario), DeduplicationKey y CorrelationId.
- ProcessingResult (record) con un enum ProcessingOutcome { Completed, Retry, DeadLetter }, el motivo opcional y tres metodos estaticos: Completed(), Retry(reason) y DeadLetter(reason).

Es importante que no dejes comentarios en el codigo, ¿ok?
```

### Prompt 3: la entrada y la fuente falsa

- **Fecha**: 01/11/26
- **Prompt**:

```text
Vamos con la entrada. Quiero una interfaz IEventSource con un unico metodo Task RunAsync(IEventProcessor processor, CancellationToken cancellationToken), y otra interfaz IEventProcessor con Task<ProcessingResult> ProcessAsync(IncomingEvent incomingEvent, CancellationToken cancellationToken). Un BackgroundService llamado Worker tiene que llamar a source.RunAsync y quedarse corriendo hasta que lo apaguen; si la fuente falla, que registre un error critico y deje el codigo de salida en 1.

Como todavia no tenemos Azure ni los otros servicios, armame FakeEventSource: recibe una lista de IncomingEvent (SampleEvents.BookingStyle(): una reserva creada, una confirmada, la creada repetida y una con JSON roto) y los entrega de a uno. Si el resultado es Retry, que reentregue con DeliveryCount + 1 hasta 3 intentos y despues lo aparte; si es DeadLetter, que lo guarde en una lista DeadLetters con su motivo.
```

### Prompt 4: el texto de los avisos

- **Fecha**: 01/11/26
- **Prompt**:

```text
Los textos de los avisos tienen que estar en el appsettings dentro de "Notifications:Templates", una por cada tipo de aviso (por ejemplo ReservaCreada y ReservaConfirmada), para poder agregar uno nuevo sin recompilar. Armá una clase NotificationOptions (patron Options) con un diccionario Templates y una clase TemplateRenderer con el metodo bool TryRender(NotificationRequest request, out string text).

En el template escribo {Campo} y se reemplaza por el dato del mismo nombre de request.Data, sin importar mayusculas. Si el dato no está, que ponga "(sin dato)". Si no hay template para el tipo, TryRender devuelve false y no lanza una excepción.
```

### Prompt 5: entender el mensaje

- **Fecha**: 01/11/26
- **Prompt**:

```text
Armame una interfaz IEventMapper con bool TryMap(IncomingEvent incomingEvent, out NotificationRequest? request, out string? error) y una clase JsonEventMapper, que sea el unico lugar que sabe como viene escrito el mensaje.

Tiene que leer un Json simple, sin campos anidados, con JsonDocument y guardarlo en un diccionario sin distinguir mayusculas (StringComparer.OrdinalIgnoreCase). Los textos pasan tal cual, los numeros y booleanos como texto, los objetos como JSON sin interpretar y los null se ignoran. Despues suma las Properties del mensaje sin pisar las del cuerpo. El Type sale del Subject y el RecipientId sale del campo que indica MappingOptions.RecipientField (sección "Notifications:Mapping"). Si falta el Subject o el destinatario, o el cuerpo no es un JSON valido, TryMap devuelve false con el motivo en error, sin lanzar excepciones.
```

### Prompt 6: no avisar dos veces

- **Fecha**: 01/11/26
- **Prompt**:

```text
Esta parte es importante: no quiero que le llegue al usuario el mismo aviso dos veces. Armame una interfaz IDeduplicationStore con bool TryBegin(string key) y void Release(string key), y una implementación en memoria (InMemoryDeduplicationStore) con un ConcurrentDictionary: TryBegin tiene que reservar la clave con TryAdd, o sea que se fije y la anote todo junto en un solo paso, y devolver false si ya estaba. Release la libera, para que el reintento de un envio fallido sí pueda avisar.

La clave la arma JsonEventMapper en el campo DeduplicationKey: el Type mas los valores de MappingOptions.DeduplicationFields (por ejemplo "ReservaId") si el mensaje trae todos; si no, "id:" mas el MessageId; y si tampoco, "body:" mas el hash SHA-256 del cuerpo.
```

### Prompt 7: decidir que hacer con cada aviso

- **Fecha**: 01/11/26
- **Prompt**:

```text
Ahora la clase NotificationHandler, con el metodo Task<ProcessingResult> HandleAsync(NotificationRequest request, CancellationToken cancellationToken). Recibe por constructor TemplateRenderer, INotificationSender, IDeduplicationStore y ILogger. El orden:
1. TryBegin de la clave. Si devuelve false, registra "Aviso repetido" como información y devuelve Completed sin enviar.
2. TryRender. Si no hay plantilla, registra una advertencia y devuelve DeadLetter con el motivo.
3. SendAsync. Si lanza una excepción (menos la cancelación), la registra como error y devuelve Retry.
4. Si esta todo ok, devuelve Completed.
En un bloque finally, si no se llegó a enviar, que llame a Release.

Armá tambien la cola de errores para lo que se aparta y calificala como warning en el log.
```

### Prompt 8: como se envía el aviso

- **Fecha**: 01/11/26
- **Prompt**:

```text
Para enviar el aviso hacé una interfaz INotificationSender con Task SendAsync(string recipientId, string text, CancellationToken cancellationToken) y una implementación ConsoleNotificationSender que use ILogger y escriba "AVISO para {recipientId}: {text}". Mas adelante se podría cambiar por mail o push registrando otra clase, sin tocar el resto. Y la composición de todo en un metodo de extensión AddNotificationCore(IServiceCollection, IConfiguration) como singleton.
```

### Prompt 9: muchos mensajes a la vez

- **Fecha**: 01/11/26
- **Prompt**:

```text
Lo probé por consola con docker run y vi una advertencia por el mensaje roto de ejemplo. Si es una reacción esperada del sistema, no hay problema; solo pregunto porque el codigo lo generó el agente y no lo escribí yo. El profe seguramente trate de emular varios tests para ver si el sistema funciona bien, capaz se tira unas 1000 request con un script, asi que quiero saber si el notificador, en su estado actual, puede procesar multiples peticiones. En teoria entiendo que si pero solo las probamos una a una.

Quiero que procese todos los mensajes que lleguen, de a uno o varios a la vez, sin perder ninguno. Además me parece que hay un problema: si el mismo mensaje llega dos veces al mismo tiempo, avisa las dos veces (se fija si ya avisó y recien despues lo anota). Corregilo: que se fije y lo anote, todo junto en un solo paso. Agregame ConcurrencyTests: 2000 mensajes secuenciales, otros 2000 con Parallel.ForEachAsync y 32 en paralelo, y un mismo mensaje repetido 20 veces a la vez con un sender lento, que tiene que avisar una sola vez.
```

## Parte 2: logs y seguimiento

### Prompt 10: Serilog y el TraceId

- **Fecha**: 01/11/26
- **Prompt**:

```text
Vamos con la parte de logs (3.f). Ya habíamos definido en el grupo que íbamos a usar TraceId para seguir una reserva, asi que vamos con esa. Instalá Serilog.Extensions.Hosting, Serilog.Settings.Configuration, Serilog.Sinks.Console y Serilog.Expressions, y configuralo con AddSerilog((services, configuration) => configuration.ReadFrom.Configuration(...).ReadFrom.Services(services)), con la sección "Serilog" en el appsettings, FromLogContext y una propiedad fija Servicio = "NotificationService".

En EventProcessor.ProcessAsync, antes de procesar, abrí un scope con logger.BeginScope con TraceId, CorrelationId y Subject. El TraceId es Properties["TraceId"] o CorrelationId si el otro falla. Todos los registros del procesamiento tienen que tenerlo.

Lo probé y en la consola el TraceId sale vacio. Creo que TraceId es un nombre reservado de Serilog por lo que googlee pero no se. Probá pasarlo a un ExpressionTemplate y dejame una prueba que compruebe que el TraceId llega como propiedad a todos los registros.
```

### Prompt 11: inicio y fin de cada aviso

- **Fecha**: 01/11/26
- **Prompt**:

```text
Quiero que por cada mensaje quede un registro de cuando empieza y otro de cuando termina con dos metodos de extensión sobre ILogger en una clase AuditLogExtensions: AuditStart(string operation) y AuditEnd(string operation, string result, long elapsedMilliseconds). Tienen que dejar estos campos: Servicio, Etapa ("Inicio" o "Fin"), Operación, TraceId, Resultado y DuracionMs. En EventProcessor usalos con la operación "ProcesarMensaje" y un Stopwatch, y el resultado es el nombre del ProcessingOutcome (por ejemplo "Fin ProcesarMensaje Resultado=Completed DuracionMs=14").

Y escribime una plantilla de logs en docs para pasarles a los otros servicios con la convención (HTTP: header x-trace-id; gRPC: metadata x-trace-id; Service Bus: propiedad TraceId y CorrelationId), un middleware de ejemplo y que tiene que hacer cada uno.
```

## Parte 3: cambio de Service Bus a HTTP

### Prompt 12: el lector de Service Bus

- **Fecha**: 02/11/26
- **Prompt**:

```text
Bueno, vamos con el lector de Service Bus, que es lo ultimo que falta. Que sea otro adaptador de IEventSource: ServiceBusEventSource con Azure.Messaging.ServiceBus, que cree un ServiceBusClient, un ServiceBusProcessor con CreateProcessor(topic, subscription, options) con AutoCompleteMessages = false y MaxConcurrentCalls configurable, y que enganche ProcessMessageAsync y ProcessErrorAsync. Para poder probarlo sin Azure, que el manejo de cada mensaje esté en una clase ServiceBusMessageHandler que reciba una interfaz IMessageSettlement (Complete, Abandon y DeadLetter), con un mapeador de ServiceBusReceivedMessage a IncomingEvent (de ahi sale el DeliveryCount).

Segun el resultado: Completed aprueba el mensaje, Retry lo abandona y DeadLetter lo manda a la cola de errores como "NoProcesable". El topic, la suscripción y la cadena de conexion salen de la sección "ServiceBus" (ServiceBusOptions), con una validación para que el servicio no arranque si falta algo, y tire que variable falta. La cadena nunca tiene que aparecer en el log.

Ojo: nosotros no tenemos acceso a Azure, solo un endpoint que nos dio el profe, asi que no tenemos como probarlo contra el servicio real. Probalo con el emulador de Service Bus.
```

### Prompt 13: el cambio a HTTP

- **Fecha**: 05/11/26
- **Prompt**:

```text
Un compañero me contó que ellos van a llamar al notificador por HTTP: un POST a /api/notifications en el puerto 5050, con timeout de 2 segundos, los headers x-trace-id y x-event-type, y el evento en JSON en el cuerpo (tipoEvento, reservaId, solicitudId, asientoId, eventoId, usuarioId, estado, fechaCreacionUtc, fechaVencimientoUtc, fechaEventoUtc y traceId) porque le preguntamos al profe y contestó que la API de notificación no es obligatoria en codigo y que conviene una llamada HTTP directa desde Booking. 

Vamos a probar cambiarlo a una llamada HTTP. Pasá el proyecto a Microsoft.NET.Sdk.Web y armame un endpoint con MapPost("/api/notifications") en una clase NotificationEndpoint, con un metodo de extensión MapNotificationEndpoint. Tiene que leer el cuerpo, armar un IncomingEvent y llamar al mismo IEventProcessor. Segun el ProcessingOutcome responde 202 (Completed, tambien si era un repetido), 400 con {"error": "..."} (DeadLetter) o 503 (Retry). Que siga usando todo lo que ya armamos, y agregá pruebas con WebApplicationFactory (para eso, public partial class Program).
```

### Prompt 14: dejarlo simple

- **Fecha**: 05/11/26
- **Prompt**:

```text
Quitá la parte de Azure Service Bus, porque no se va a utilizar con esta modificación: ServiceBusEventSource, el handler, el mapeador, las opciones, el paquete Azure.Messaging.ServiceBus y sus pruebas. Y lo que solo servía para alimentarlo: FakeEventSource, SampleEvents, IEventSource y el Worker. Tiene que ser lo mas simple y directo posible, y las pruebas tienen que seguir pasando. 

Sacá MessageId y DeliveryCount de IncomingEvent (eran de Service Bus) y el nivel del MessageId de la clave de duplicados; renombrá ProcessingOutcome.DeadLetter y ProcessingResult.DeadLetter(reason) a Rejected, y que los mensajes de log digan "se rechaza" y no "cola de errores".

Detalles de la entrada: una clase HttpEventMapper con ToIncomingEvent(string? eventTypeHeader, string? traceIdHeader, string body) que tome el tipo del header x-event-type y, si no viene, el campo tipoEvento del cuerpo; con el TraceId igual (primero el header x-trace-id y, si no, el campo traceId). Si el header y el cuerpo dicen cosas distintas, que gane el header, y que no se rompa si el cuerpo no es un JSON. Con ConfigureKestrel, Limits.MaxRequestBodySize de 64 KB (413 si lo supera), y que solo acepte POST (405 con otro metodo).
```

## Parte 4: cierre

### Prompt 15: Docker y prueba rapida

- **Fecha**: 05/11/26
- **Prompt**:

```text
Armá el Dockerfile (multi-stage: sdk:10.0 para compilar y aspnet:10.0 para correr, con ASPNETCORE_URLS=http://+:5050, EXPOSE 5050 y USER $APP_UID) y fijá el puerto 5050 con applicationUrl en launchSettings.json para correrlo local. Y un script scripts/smoke-test.sh que construya la imagen, levante el contenedor con -p 127.0.0.1:5050:5050, espere a que responda y le mande llamadas de verdad con curl: un evento valido (202), el mismo repetido (202 y un solo "AVISO para" en el log), uno invalido (400 y "Resultado=Rejected" en el log), uno de mas de 64 KB (413) y un GET (405). Que compruebe el TraceId del header en el log, diga OK o FALLO en cada una.
```

### Prompt 16: README y diagrama

- **Fecha**: 05/11/26
- **Prompt**:

```text
Para cerrar, explicame como quedó el notificador con un diagrama de componentes, que quede claro de donde recibe y adonde envía. Algo parecido a lo que hizo el equipo de Booking: las capas de presentación (NotificationEndpoint, HttpEventMapper y el limite de Kestrel), logica (EventProcessor, JsonEventMapper, NotificationHandler, TemplateRenderer e IDeduplicationStore) y salida (INotificationSender y ConsoleNotificationSender), con los pasos numerados de una llamada (punto 3.i), la llamada de Booking a la izquierda y el log a la salida. Y escribime el README del servicio: como funciona, como es la llamada HTTP (headers, cuerpo y respuestas), como se configura (Notifications:Mapping y Notifications:Templates) y como se ejecuta.
```

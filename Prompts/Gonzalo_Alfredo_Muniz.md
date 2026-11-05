# Historial de Prompts - Gonzalo Alfredo Muñiz

## Prompt 1
> Te paso el contexto antes de pedirte nada. Es el TP de Programación IV: un sistema de reserva de asientos con microservicios en C# (.NET 10). La consigna está en Trabajo_practico.pdf y el reparto de tareas en la asignación de roles.
> Me toca la dupla 3, y la hago solo porque mi compañero dejó el grupo: la Payment gRPC API (N-Layer, dominio anémico, base propia) y el cliente gRPC con resiliencia (Timeout, Retry y Circuit Breaker con Polly) que la llama desde Booking.
> Reglas para trabajar:
> 1. No escribas código todavía. Primero quiero que leas la consigna y me devuelvas qué puntos me tocan a mí (3.c, 3.e, 3.f, 3.i, 3.j) y qué tengo que entregar en cada uno.
> 2. Cada propuesta tiene que traer la justificación y, si hay más de un camino, las alternativas con sus ventajas y desventajas. Las decisiones las tomo yo.
> 3. Trabajamos por fases y en mi rama (feature-3-Payment-gRPC-API), sin tocar código de otras duplas sin avisar.
> 4. Ningún secreto (connection strings, client secrets) va al repositorio.

## Prompt 2
> Antes de diseñar, quiero ver qué hizo el resto para no duplicar trabajo ni chocar. Roman subió en feature-2 un payment.proto y un gateway con Polly, y Marcos subió el compose del grupo y una plantilla de logs con TraceId.
> Analizá el gateway de Roman contra lo que pide el punto 3.j y listame qué cumple y qué no, con el motivo. En particular quiero saber si el circuit breaker funcionaría tal como están registradas las políticas, y si el timeout corta de verdad la llamada.
> Con lo que encuentres, ¿me conviene partir mi rama de develop o de feature-2?

## Prompt 3
> Armame una tabla con las decisiones de diseño pendientes: estilo del contrato, dónde vive el .proto, cómo viaja la plata, la base de Payment, la estructura del proyecto, cómo obtener y mandar el token, la clave de idempotencia y la convención de TraceId. Para cada una, las opciones, cuál recomendás y por qué.
> Tomo estas: contract-first con un único .proto versionado en una carpeta compartida, porque la cátedra mostró ese enfoque en el ejemplo con JWT y evita dos copias que se desincronizan; el monto en centavos (int64) en vez de double, porque double no representa exacto valores como 0,10; y adopto la convención de TraceId de la dupla 4 para no tener dos criterios en el sistema.
> El resto de las recomendaciones me cierran. Antes de seguir, decime si alguna de estas decisiones contradice algo de la consigna.

## Prompt 4
> Arranquemos por la DAL de Payment: la entidad Pago (solo datos, dominio anémico), el DbContext con PostgreSQL y la migración inicial. Cada cobro tiene que quedar registrado, aprobado o rechazado, como historial.
> Un timeout de Booking puede llegar después de que Payment ya cobró, y Polly va a reintentar. Quiero que la garantía de no cobrar dos veces esté en la base y no solo en el código: proponé cómo hacerlo y qué pasa si dos intentos llegan al mismo tiempo.

## Prompt 5
> Ahora la BLL. Necesito un banco simulado determinístico, para poder repetir cada escenario en la demo: un límite por cobro, una lista de usuarios sin fondos y un retardo configurable, todo desde appsettings o variables de entorno, sin tocar código.
> El servicio de pagos tiene que aplicar la idempotencia. Si llega la misma reserva con otro monto, no la trates como un reintento: ¿qué corresponde devolver en ese caso y por qué?
> Separá bien las capas: la BLL no tiene que saber que del otro lado hay gRPC.

## Prompt 6
> Vamos con Presentation: el servicio gRPC, el interceptor de TraceId con la plantilla de la dupla 4 y la validación del token de Keycloak.
> Probé pedir un token del realm del grupo y no trae audiencia. No quiero desactivar la seguridad para que funcione: firma, emisor y vencimiento se validan siempre, y además solo tiene que poder cobrar el cliente booking-api. Proponé cómo dejar la audiencia configurable sin abrir un agujero.
> Definí también qué status gRPC devuelve cada error. Booking va a reintentar solo lo transitorio, así que esa elección es parte del contrato.

## Prompt 7
> Armame el Dockerfile de Payment y un compose que funcione solo y también sumado al del grupo, con las mismas convenciones que usó la dupla 4 (base sin puertos publicados, passwords con valor por defecto, configuración por variables de entorno).
> No me alcanza con que compile. Quiero probarlo en Docker contra un Keycloak real, en estos casos: sin token, token de booking-api sin audiencia, token de otro cliente, fondos insuficientes, la misma reserva dos veces y Payment lento. Mostrame qué quedó guardado en la base en cada caso.

## Prompt 8
> Sumá tests automáticos: unitarios para el banco y la idempotencia, y de integración que llamen al servicio gRPC de verdad, con tokens firmados en el test (válido, vencido, de otro cliente, con otra firma).
> La carrera de dos cobros simultáneos quiero verla contra un Postgres real y no contra un mock, porque es justamente lo que garantiza el índice. Que ese test se pueda saltear si no hay base disponible.

## Prompt 9
> Payment está cerrado. Antes de pasar a Booking, hacé una revisión crítica de todo lo que hicimos, buscando bugs y cosas que no cumplan la consigna. No cambies nada: listame cada problema con su gravedad y el escenario concreto en el que falla, y después decidimos qué se corrige.

## Prompt 10
> Vamos con el cliente en Booking. Reescribamos el gateway de Roman sobre su base, con estas condiciones:
> - Un pipeline de Polly v8 único para toda la aplicación, porque si se arma uno por request el circuit breaker nunca acumula fallas.
> - De afuera hacia adentro: timeout total, reintentos con espera exponencial y jitter, circuit breaker y timeout por intento. Justificá cada valor contra el tiempo que el bus le da al mensaje.
> - Reintentar solo lo transitorio. ¿Por qué no conviene reintentar DeadlineExceeded o Internal en un cobro?
> - El token se pide fuera del pipeline, para que una falla de Keycloak no abra el circuito de Payment.
> No se tocan BookingService ni IPaymentGateway: el gateway devuelve el mismo DTO y nunca lanza excepciones.

## Prompt 11
> El test del timeout por intento me genera una duda: ¿el cliente gRPC devuelve la cancelación de una forma que Polly reconozca como timeout? Verificalo en la documentación de grpc-dotnet y de Polly v8, y si hace falta ajustar la configuración del canal, explicame por qué.
> Agregá también un test que levante Booking con su Program.cs real, para confirmar que el pipeline y el token quedan registrados como Singleton.

## Prompt 12
> Probemos los escenarios de la demo con el gateway real contra Payment en Docker: happy path, fondos insuficientes, Payment caído, otra reserva con el circuito abierto, la recuperación y Payment lento.
> Quiero probar además una caída larga: Payment apagado un minuto y medir cuánto tarda en volver a confirmar reservas. Si tarda más que el corte del circuito, quiero entender por qué antes de corregir nada.

## Prompt 13
> Para la entrega: el README de Payment, el diagrama de arquitectura interna (3.i) y los diagramas de secuencia del cobro y de la caída (3.j), un programa chico para mostrar la resiliencia en la defensa, y la evidencia de los escenarios con lo que quedó en la base.
> Después contrastá cada afirmación de la documentación con el código y con la evidencia. Prefiero corregir un documento antes que defender algo que no es cierto.

## Prompt 14
> Antes de abrir el PR: clonalo limpio desde GitHub y comprobá que compila, que pasan los tests y que la imagen se construye. Revisá además el diff completo contra develop buscando secretos.
> El PR va como borrador, porque mi rama sale de feature-2 y tiene que entrar después del de Booking. En la descripción listá los archivos de Booking que se tocan, para que la dupla 2 los pueda revisar.

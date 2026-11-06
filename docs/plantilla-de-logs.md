# Plantilla de logs y trazabilidad

Punto 3.f del trabajo práctico: logs de auditoría para identificar el inicio y el fin de una cadena de sucesos en cada microservicio. Esta plantilla la provee la dupla 4 para que los demás servicios la conecten.

## Qué resuelve

Cada servicio registra el **inicio** y el **fin** de su parte del trabajo, y todos los registros llevan el mismo `TraceId`. Con eso se puede seguir una reserva de punta a punta, desde que Availability la inicia hasta que el notificador avisa al usuario.

## La convención

| Tema | Regla |
|---|---|
| Nombre | `TraceId`, como se definió en el grupo |
| Quién lo crea | Availability, cuando inicia la solicitud. Valor recomendado: el `SolicitudId` |
| Regla principal | **Quien recibe un `TraceId` lo reutiliza.** Solo genera uno nuevo quien inicia la cadena |
| HTTP | Header `x-trace-id` |
| gRPC | Metadata `x-trace-id` |
| Service Bus | Propiedad de aplicación `TraceId`, y `CorrelationId` con el mismo valor |
| En los logs | Propiedad `TraceId` en cada registro, con `Serilog` |

### Campos del log de auditoría

| Campo | Contenido |
|---|---|
| `Servicio` | Nombre del servicio, configurado una vez en `appsettings.json` |
| `Etapa` | `Inicio` o `Fin` |
| `Operacion` | Qué se está haciendo, por ejemplo `POST /api/reservas` o `ProcesarMensaje` |
| `TraceId` | El identificador de la cadena |
| `Resultado` | Cómo terminó: código HTTP, o `Completed`, `Retry`, `DeadLetter` |
| `DuracionMs` | Duración de la operación en milisegundos |

### Ejemplo de salida

Una llamada del servicio A al B. El `TraceId` es el mismo en los dos:

```text
23:41:01 INF [PruebaPlantilla] [TraceId=reserva-demo-123] Inicio GET /a
23:41:01 INF [PruebaPlantilla] [TraceId=reserva-demo-123] Servicio A: llamo al servicio B
23:41:01 INF [PruebaPlantilla] [TraceId=reserva-demo-123] Inicio GET /b
23:41:01 INF [PruebaPlantilla] [TraceId=reserva-demo-123] Servicio B: recibí la llamada
23:41:01 INF [PruebaPlantilla] [TraceId=reserva-demo-123] Fin GET /b Resultado=200 DuracionMs=3
23:41:01 INF [PruebaPlantilla] [TraceId=reserva-demo-123] Fin GET /a Resultado=200 DuracionMs=105
```

El servicio de notificaciones ya la usa. Es la implementación de referencia:

```text
23:37:28 INF [NotificationService] [TraceId=a1f3c0de5b7e4c9f8a2d6e1b3c4d5e6f] Inicio ProcesarMensaje
23:37:28 INF [NotificationService] [TraceId=a1f3c0de5b7e4c9f8a2d6e1b3c4d5e6f] AVISO para usuario-demo: Tu reserva 7c9e6679-7425-40de-944b-e07fc1f90ae7 quedó retenida hasta 2026-11-01T20:05:00Z. Pagá antes de que venza para confirmarla.
23:37:28 INF [NotificationService] [TraceId=a1f3c0de5b7e4c9f8a2d6e1b3c4d5e6f] Fin ProcesarMensaje Resultado=Completed DuracionMs=14
```

## Cómo conectarla

### 1. Paquetes

Servicio web (Availability, Booking, Payment):

```bash
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Expressions
```

Servicio sin web (Worker):

```bash
dotnet add package Serilog.Extensions.Hosting
dotnet add package Serilog.Settings.Configuration
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Expressions
```

Para gRPC, `Grpc.AspNetCore` (Payment) ya trae lo que necesita el interceptor.

### 2. Configuración en `appsettings.json`

Copiar tal cual y cambiar el nombre del servicio:

```json
{
  "Serilog": {
    "Using": [
      "Serilog.Sinks.Console",
      "Serilog.Expressions"
    ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning"
      }
    },
    "Enrich": [
      "FromLogContext"
    ],
    "Properties": {
      "Servicio": "NombreDelServicio"
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "formatter": {
            "type": "Serilog.Templates.ExpressionTemplate, Serilog.Expressions",
            "template": "{@t:HH:mm:ss} {@l:u3} [{Servicio}] [TraceId={TraceId}] {@m}\n{@x}"
          }
        }
      }
    ]
  }
}
```

> **Importante:** en una plantilla de salida común de Serilog (`outputTemplate`), `{TraceId}` es un nombre reservado: muestra el identificador interno de .NET y sale vacío. Por eso la plantilla usa `ExpressionTemplate`, donde `TraceId` sí es nuestra propiedad.

### 3. Servicio web: `Program.cs`

```csharp
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

app.UseMiddleware<TraceIdMiddleware>();
```

El `UseMiddleware` va lo más arriba posible, antes de mapear los endpoints.

Worker (como el notificador):

```csharp
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));
```

### 4. Middleware: recibe o crea el `TraceId` y registra inicio y fin

```csharp
using System.Diagnostics;
using Serilog.Context;

public sealed class TraceIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "x-trace-id";
    public const string PropertyName = "TraceId";

    public async Task InvokeAsync(HttpContext context, ILogger<TraceIdMiddleware> logger)
    {
        var traceId = context.Request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(traceId))
        {
            traceId = Guid.NewGuid().ToString();
        }

        context.Items[PropertyName] = traceId;
        context.Response.Headers[HeaderName] = traceId;

        using (LogContext.PushProperty(PropertyName, traceId))
        {
            var operation = $"{context.Request.Method} {context.Request.Path}";
            var stopwatch = Stopwatch.StartNew();
            logger.LogInformation("{Etapa} {Operacion}", "Inicio", operation);
            try
            {
                await next(context);
            }
            finally
            {
                logger.LogInformation(
                    "{Etapa} {Operacion} Resultado={Resultado} DuracionMs={DuracionMs}",
                    "Fin", operation, context.Response.StatusCode, stopwatch.ElapsedMilliseconds);
            }
        }
    }
}
```

### 5. Llamadas HTTP salientes: reenviar el `TraceId`

```csharp
public sealed class TraceIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var traceId = accessor.HttpContext?.Items[TraceIdMiddleware.PropertyName] as string;
        if (traceId is not null && !request.Headers.Contains(TraceIdMiddleware.HeaderName))
        {
            request.Headers.Add(TraceIdMiddleware.HeaderName, traceId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
```

Registro:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddTransient<TraceIdHandler>();
builder.Services.AddHttpClient("destino").AddHttpMessageHandler<TraceIdHandler>();
```

Con un cliente tipado, `AddHttpClient<MiCliente>()` también acepta `.AddHttpMessageHandler<TraceIdHandler>()`.

### 6. gRPC

Cliente (Booking ya manda el header; la diferencia es usar el `TraceId` recibido en lugar de crear uno nuevo en cada llamada):

```csharp
var headers = new Metadata { { "x-trace-id", traceId } };
var response = await client.ProcesarPagoAsync(request, headers: headers, deadline: DateTime.UtcNow.AddSeconds(3));
```

Servidor (Payment). Interceptor:

```csharp
using Grpc.Core;
using Grpc.Core.Interceptors;
using Serilog.Context;

public sealed class TraceIdServerInterceptor : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var traceId = context.RequestHeaders.GetValue("x-trace-id");
        if (string.IsNullOrWhiteSpace(traceId))
        {
            traceId = Guid.NewGuid().ToString();
        }

        using (LogContext.PushProperty("TraceId", traceId))
        {
            return await continuation(request, context);
        }
    }
}
```

Registro:

```csharp
builder.Services.AddGrpc(options => options.Interceptors.Add<TraceIdServerInterceptor>());
```

### 7. Service Bus

```csharp
using Azure.Messaging.ServiceBus;
using Serilog.Context;

public static class ServiceBusTrace
{
    public static ServiceBusMessage WithTraceId(this ServiceBusMessage message, string traceId)
    {
        message.CorrelationId = traceId;
        message.ApplicationProperties["TraceId"] = traceId;
        return message;
    }

    public static string? TraceIdOf(ServiceBusReceivedMessage message) =>
        message.ApplicationProperties.TryGetValue("TraceId", out var value)
            ? value?.ToString()
            : message.CorrelationId;

    public static IDisposable PushTraceId(ServiceBusReceivedMessage message) =>
        LogContext.PushProperty("TraceId", TraceIdOf(message));
}
```

Al enviar:

```csharp
var message = new ServiceBusMessage(body).WithTraceId(traceId);
```

Al recibir, dentro del procesamiento del mensaje:

```csharp
using (ServiceBusTrace.PushTraceId(args.Message))
{
    await ProcesarMensajeAsync(args.Message);
}
```

El notificador acepta mensajes sin `TraceId`: en ese caso usa el `CorrelationId`.

## Qué cambia en cada servicio

| Servicio | Qué falta |
|---|---|
| **Availability** (dupla 1) | Crear el `TraceId` al iniciar la solicitud (puede ser el `SolicitudId`) y mandarlo en el mensaje a Service Bus y en los pedidos HTTP a Booking. Hoy no publica a Service Bus ni fija ningún identificador |
| **Booking** (dupla 2) | Ya lee la propiedad `TraceId` de los mensajes entrantes (`ServiceBusListener`). Falta pasarla hacia abajo y usarla en lugar de `Guid.NewGuid()` en `PaymentGrpcGateway` y en `ServiceBusEventPublisher`. Hoy cada llamada y cada evento llevan un `TraceId` distinto y la cadena se corta |
| **Payment** (dupla 3) | Agregar Serilog y el interceptor del servidor. Todavía no hay código |
| **Notificaciones** (dupla 4) | Hecho. Usa el `TraceId` del mensaje o, si falta, el `CorrelationId` |

## Qué tramos de la cadena cubre hoy

Estado al 2026-11-01, según el código de cada rama.

| Tramo | Estado |
|---|---|
| Availability a Booking, por Service Bus | Falta. Availability todavía no publica |
| Availability a Booking, por HTTP | Falta conectar el reenvío del `TraceId` (punto 5) |
| Booking a Payment, por gRPC | Booking manda `x-trace-id`, pero con un valor nuevo en cada llamada |
| Booking al notificador, por Service Bus | Cubierto. El notificador registra el `TraceId` del mensaje o el `CorrelationId` |

### Si un servicio no la adopta

La reserva se puede seguir igual por su `SolicitudId`, que ya aparece en los logs de Booking y viaja como `CorrelationId` en los eventos al notificador. Con Docker Compose:

```bash
docker compose logs | grep <SolicitudId>
```

No es un `TraceId` propagado, pero reconstruye la cadena donde cada servicio registre ese dato.

## Cómo se verificó

- Los fragmentos de este documento se compilaron en un proyecto web de prueba con .NET 10, Serilog.AspNetCore 10, Grpc.AspNetCore 2.85 y Azure.Messaging.ServiceBus 7.21.
- La parte HTTP se probó de punta a punta: dos endpoints que se llaman entre sí mantienen el `TraceId` recibido, lo devuelven en el header de la respuesta y lo generan cuando falta.
- El interceptor de gRPC y el código de Service Bus se compilaron pero no se ejecutaron contra servicios reales. La línea del cliente gRPC es la que Booking ya usa y no está en esa compilación.
- El servicio de notificaciones tiene pruebas automáticas de la propiedad `TraceId`, de los campos de auditoría y de la plantilla de salida.

# Sistema de reserva de asientos, Grupo 6

Trabajo práctico de Programación IV. Microservicios en .NET 10: Availability, Booking, Payment, Notificaciones y Keycloak.

## Requisitos

Docker Desktop y .NET 10 SDK.

## Levantarlo con Docker

1. Crear un archivo `.env` en la raíz del repositorio y pegar este contenido:

```env
# Azure Service Bus: lo usan Availability y Booking
AZURE_SERVICE_BUS_CONNECTION_STRING=Endpoint=sb://grupo6.servicebus.windows.net/;SharedAccessKeyName=All;SharedAccessKey=2Wdu2+qu11HcnppPZfz3c33tqo62PxoA1+ASbEmADvw=;EntityPath=grupo6-booking-topic

# MongoDB: lo usa Availability
MONGODB_CONNECTION_STRING=mongodb+srv://jonathanmarinobellin_db_user:W3UdLkysEKyZ4a1a@availabilitysite.v4fttxd.mongodb.net/AvailabilityDb

# Keycloak y Payment
KEYCLOAK_REALM=reservas-realm
PAYMENT_DB_PASSWORD=payment-dev
PAYMENT_VALIDATE_AUDIENCE=true
PAYMENT_RETARDO_MS=0

# Google: inicio de sesión con cuenta de Google en Keycloak
GOOGLE_CLIENT_SECRET=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
```

2. Ejecutar desde la raíz:

```bash
docker compose -f docker-keycloak-booking-payment.yaml up -d --build --scale booking=2
```

3. Abrir http://localhost:5215.

## Cómo probarlo

1. Elegir un evento y un asiento disponible.
2. Tocar **Continuar con Google**. La cuenta debe estar habilitada en la app de Google si esta sigue en modo de prueba.
3. Enviar la solicitud y seguir su estado en **Mis solicitudes**.

| Prueba | Resultado esperado |
|---|---|
| Reservar en **Conferencia de prueba**, $15.000 | Reserva confirmada y asiento ocupado |
| Reservar en **Recital de prueba**, $22.000 | Rechazada por pago fallido: supera el límite de $20.000 |
| Apagar Payment y reservar | Rechazada por pago no disponible, sin que se caiga Booking |

Para apagar y volver a prender Payment:

```bash
docker compose -f docker-keycloak-booking-payment.yaml stop payment
```

```bash
docker compose -f docker-keycloak-booking-payment.yaml start payment
```

El detalle y las pruebas realizadas están en [`docs/happy-path.md`](docs/happy-path.md).

## Correrlo sin Docker

Pegar cada bloque en los User Secrets del proyecto. En Visual Studio: clic derecho en el proyecto, **Manage User Secrets**.

**`AvailabilitySite.Web`:** Keycloak, MongoDB y Service Bus.

```json
{
  "KeycloakService": {
    "ClientSecret": "hkXvGNerqp5S7lW52tcohXfQHcjLHR82sODSmWEyne270WAYs5WmB7GmXoqtaELr5CTtiFiFr3CVvlORzHl7Lo",
    "ClientId": "availability-api"
  },
  "MongoDB": {
    "ConnectionString": "mongodb+srv://jonathanmarinobellin_db_user:W3UdLkysEKyZ4a1a@availabilitysite.v4fttxd.mongodb.net/AvailabilityDb",
    "DatabaseName": "AvailabilityDb"
  },
  "ServiceBus": {
    "ConnectionString": "Endpoint=sb://grupo6.servicebus.windows.net/;SharedAccessKeyName=All;SharedAccessKey=2Wdu2+qu11HcnppPZfz3c33tqo62PxoA1+ASbEmADvw=;EntityPath=grupo6-booking-topic"
  }
}
```

**`BookingAPI`:** Keycloak y Service Bus.

```json
{
  "Authentication": {
    "ClientSecret": "MGIdI4Tow7vEiTGHaU8s7xUEyrgGvXC7jSijbb5rDdqFGTqXx1MLYOMIUNeoKqsM54vMw0MtwqJa1bbS8G0QYq"
  },
  "AzureServiceBus": {
    "ConnectionString": "Endpoint=sb://grupo6.servicebus.windows.net/;SharedAccessKeyName=All;SharedAccessKey=2Wdu2+qu11HcnppPZfz3c33tqo62PxoA1+ASbEmADvw=;EntityPath=grupo6-booking-topic"
  }
}
```

**`PaymentAPI`:** no necesita User Secrets. En Development usa su base local en `localhost:5433`.

Con el Keycloak de este repositorio, `ClientSecret` debe ser `**********` en Availability y en Booking: es el valor que importa `realm-export.json`.

## Qué es cada dato

| Dato | Dónde va |
|---|---|
| `ServiceBus:ConnectionString` | User Secrets de Availability |
| `AzureServiceBus:ConnectionString` | User Secrets de Booking |
| `KeycloakService:ClientSecret` | User Secrets de Availability |
| `Authentication:ClientSecret` | User Secrets de Booking |
| `MongoDB:ConnectionString` | User Secrets de Availability |
| `KEYCLOAK_REALM` | `.env` |
| `PAYMENT_DB_PASSWORD` | `.env` |
| `PAYMENT_VALIDATE_AUDIENCE` | `.env` |
| `PAYMENT_RETARDO_MS` | `.env` |
| `GOOGLE_CLIENT_SECRET` | `.env` |
| `PAYMENT_LIMITE_CENTAVOS` | `.env`, opcional. Por defecto, 2000000 |

## Documentación

- [Arquitectura y tabla del punto 1](docs/arquitectura.md)
- [Happy path y flujos de fallo](docs/happy-path.md)
- [Plantilla de logs](docs/plantilla-de-logs.md)
- [Availability](docs/feature-1-Availability.md)
- [Booking](docs/feature-2-booking.md)
- [Payment](docs/feature-3-pagos.md)
- [Authorization Server](docs/feature-4-authorization-server.md)
- [Notificaciones](TP-ProgramacionIV/NotificationService/README.md)

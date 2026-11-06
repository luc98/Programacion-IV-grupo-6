# Happy path y flujos de fallo

Punto 3.j del trabajo práctico. Los resultados son de pruebas reales con Docker Compose, Azure Service Bus y dos réplicas de Booking. Las pruebas de fallas usaron MongoDB local. La prueba final, desde un clon limpio con Azure, Atlas y Google reales, cubrió reserva confirmada, pago fallido, Payment caído y recuperación.

## Cómo levantarlo

Seguir el [README](../README.md).

## Camino feliz

| Paso | Qué pasa | Dónde se ve |
|---|---|---|
| 1. Consulta | Un visitante ve eventos y asientos sin iniciar sesión | Availability |
| 2. Autenticación | Al elegir un asiento, inicia sesión con Google a través de Keycloak | Keycloak y Google |
| 3. Solicitud | Availability guarda la solicitud y la publica en Azure Service Bus | Log `SolicitarReserva` |
| 4. Procesamiento | Una réplica de Booking crea la reserva Retenida | Log `ProcesarSolicitudReserva` |
| 5. Cobro | Booking llama a Payment por gRPC con su token | Logs de Booking y Payment |
| 6. Resultado | Booking confirma la reserva y envía dos avisos con token | Log de Notifications |
| 7. Seguimiento | El usuario ve el estado final y el asiento queda ocupado | **Mis solicitudes** |

Para seguir una reserva, buscar su número de solicitud, que también es el `TraceId`:

```bash
docker compose -f docker-keycloak-booking-payment.yaml logs --no-color | grep <numero-de-solicitud>
```

## Resultados

| Prueba | Resultado | Evidencia |
|---|---|---|
| Reserva con cobro aprobado | Confirmada, con los dos avisos autenticados | [01](evidencia/01-reserva-confirmada.txt) |
| Recital de $22.000, límite de $20.000 | Rechazada por `PagoFallido`; el asiento vuelve a estar libre | [02](evidencia/02-pago-fallido.txt) |
| Payment caído | Rechazada por `PagoNoDisponible`; las dos réplicas siguen vivas | [03](evidencia/03-payment-caido.txt) |
| Notifications caído | Confirmada; Booking deja la advertencia en el log | [04](evidencia/04-notificaciones-caidas.txt) |
| 6 solicitudes simultáneas al mismo asiento | 1 confirmada y 5 rechazadas por `AsientoOcupado` | [05](evidencia/05-concurrencia-dos-replicas.txt) |
| Booking caído | La página pública responde y la solicitud se procesa al volver | [06](evidencia/06-booking-caido.txt) |
| Reserva retenida sin resultado | Ocupa el asiento, pasa a `Vencida` y lo libera | [07](evidencia/07-vencimiento.txt) |
| Llamada a Notifications sin token | Responde 401 | Prueba manual |

## Flujo de fallo para mostrar

1. Apagar Payment:

```bash
docker compose -f docker-keycloak-booking-payment.yaml stop payment
```

2. Reservar un asiento de la Conferencia.
3. Ver en **Mis solicitudes** el rechazo por pago no disponible.
4. Volver a prender Payment y esperar unos 20 segundos, hasta que se cierre el circuito:

```bash
docker compose -f docker-keycloak-booking-payment.yaml start payment
```

Booking no se cae: la reserva termina rechazada y el asiento queda libre.

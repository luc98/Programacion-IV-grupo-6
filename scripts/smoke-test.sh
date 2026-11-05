#!/usr/bin/env bash
set -uo pipefail

cd "$(dirname "$0")/.."

IMAGEN=notification-service:smoke
CONTENEDOR=notification-service-smoke
URL=http://127.0.0.1:5050/api/notifications
fallos=0

paso() { echo "[$(date +%H:%M:%S)] $*"; }
ok() { echo "  OK    $*"; }
mal() { echo "  FALLO $*"; fallos=$((fallos + 1)); }

esperar() {
  local descripcion="$1" intentos="$2"
  shift 2
  for _ in $(seq 1 "$intentos"); do
    if "$@" >/dev/null 2>&1; then ok "$descripcion"; return 0; fi
    sleep 1
  done
  mal "$descripcion"
  return 1
}

log_contiene() {
  docker logs "$CONTENEDOR" 2>&1 | grep -q -- "$1"
}

cuenta_en_log() {
  docker logs "$CONTENEDOR" 2>&1 | grep -c -- "$1" || true
}

codigo_http() {
  curl -s -o /dev/null -w '%{http_code}' -X POST "$URL" -H 'content-type: application/json' "$@"
}

limpiar() {
  docker rm -f "$CONTENEDOR" >/dev/null 2>&1 || true
}
trap limpiar EXIT

paso "Construyendo y levantando el notificador"
limpiar
docker build -q -t "$IMAGEN" TP-ProgramacionIV/NotificationService >/dev/null || { mal "docker build"; exit 1; }
docker run -d --name "$CONTENEDOR" -p 127.0.0.1:5050:5050 "$IMAGEN" >/dev/null || { mal "docker run"; exit 1; }

paso "Servicio de notificaciones"
esperar "El notificador responde en http://127.0.0.1:5050" 30 curl -s -o /dev/null "$URL"

RESERVA=$(cat /proc/sys/kernel/random/uuid)
CUERPO="{\"tipoEvento\":\"ReservaCreada\",\"reservaId\":\"$RESERVA\",\"usuarioId\":\"usuario-http\",\"estado\":\"Retenida\",\"fechaVencimientoUtc\":\"2026-11-05T18:05:00Z\",\"traceId\":\"trace-http-1\"}"

[ "$(codigo_http -H 'x-event-type: ReservaCreada' -H 'x-trace-id: trace-http-1' -d "$CUERPO")" = "202" ] && ok "Un evento válido responde 202" || mal "Se esperaba 202 con un evento válido"
[ "$(codigo_http -H 'x-event-type: ReservaCreada' -d "$CUERPO")" = "202" ] && ok "El mismo evento repetido responde 202" || mal "Se esperaba 202 con el evento repetido"
[ "$(cuenta_en_log "AVISO para usuario-http")" = "1" ] && ok "Avisó una sola vez al usuario" || mal "Se esperaba un solo aviso para usuario-http"
log_contiene "TraceId=trace-http-1" && ok "Los registros llevan el TraceId de la cabecera" || mal "No aparece TraceId=trace-http-1 en el log"
[ "$(codigo_http -d 'esto no es json')" = "400" ] && ok "Un cuerpo inválido responde 400" || mal "Se esperaba 400 con un cuerpo inválido"
log_contiene "Resultado=Rejected" && ok "El rechazo queda en el log" || mal "No aparece Resultado=Rejected en el log"
[ "$(codigo_http -H 'x-event-type: ReservaCreada' -d "{\"usuarioId\":\"u\",\"relleno\":\"$(head -c 70000 /dev/zero | tr '\0' x)\"}")" = "413" ] && ok "Un cuerpo de más de 64 KB responde 413" || mal "Se esperaba 413 con un cuerpo muy grande"
[ "$(curl -s -o /dev/null -w '%{http_code}' "$URL")" = "405" ] && ok "GET responde 405" || mal "Se esperaba 405 con GET"

echo
if [ "$fallos" -eq 0 ]; then
  paso "PRUEBA OK"
else
  paso "PRUEBA CON $fallos FALLO(S)"
  exit 1
fi

#!/usr/bin/env bash
set -uo pipefail

cd "$(dirname "$0")/.."

fallos=0

paso() { echo "[$(date +%H:%M:%S)] $*"; }
ok() { echo "  OK    $*"; }
mal() { echo "  FALLO $*"; fallos=$((fallos + 1)); }

esperar() {
  local descripcion="$1" intentos="$2"
  shift 2
  for _ in $(seq 1 "$intentos"); do
    if "$@" >/dev/null 2>&1; then ok "$descripcion"; return 0; fi
    sleep 2
  done
  mal "$descripcion"
  return 1
}

estado_saludable() {
  [ "$(docker inspect -f '{{.State.Health.Status}}' "$(docker compose ps -q "$1")")" = "healthy" ]
}

log_contiene() {
  docker compose logs --no-color notification-service 2>&1 | grep -q -- "$1"
}

cuenta_en_log() {
  docker compose logs --no-color notification-service 2>&1 | grep -c -- "$1" || true
}

paso "Levantando los servicios"
docker compose up -d --build || { mal "docker compose up"; exit 1; }

paso "Infraestructura"
esperar "La base de Keycloak está saludable" 30 estado_saludable keycloak-db
esperar "La base de Booking está saludable" 30 estado_saludable booking-db
esperar "Keycloak responde en http://127.0.0.1:8080" 60 curl -fs http://127.0.0.1:8080/realms/master

paso "Servicio de notificaciones"
esperar "Procesó los mensajes de ejemplo" 30 log_contiene "se entregaron todos los mensajes"

[ "$(cuenta_en_log 'AVISO para')" = "2" ] && ok "Envió 2 avisos al usuario" || mal "Se esperaban 2 avisos"
[ "$(cuenta_en_log 'Aviso repetido')" = "1" ] && ok "Descartó 1 aviso repetido" || mal "Se esperaba 1 aviso repetido"
[ "$(cuenta_en_log 'Resultado=DeadLetter')" = "1" ] && ok "Mandó 1 mensaje inválido a la cola de errores" || mal "Se esperaba 1 mensaje en la cola de errores"
log_contiene "TraceId=a1f3c0de5b7e4c9f8a2d6e1b3c4d5e6f" && ok "Los registros llevan el TraceId" || mal "No aparece el TraceId en el log"

echo
if [ "$fallos" -eq 0 ]; then
  paso "PRUEBA OK"
else
  paso "PRUEBA CON $fallos FALLO(S)"
  exit 1
fi

#!/usr/bin/env bash
# Stress test via curl — não precisa de dotnet local.
#
# OpenShift (mercantil-dev):
#   ./CrudApp/stress-example.sh              # memória (padrão)
#   ./CrudApp/stress-example.sh cpu          # CPU
#
# Parâmetros memória:
#   NS=mercantil-dev MB=120 DURATION=600 ./CrudApp/stress-example.sh
#
# Parâmetros CPU:
#   PERCENT=80 DURATION=120 ./CrudApp/stress-example.sh cpu
#   THREADS=2 DURATION=60 ./CrudApp/stress-example.sh cpu

set -euo pipefail

NS="${NS:-mercantil-dev}"
DEPLOY="${DEPLOY:-my-dotnet5-crud}"
APP_LABEL="${APP_LABEL:-app=${DEPLOY}}"
MB="${MB:-120}"
PERCENT="${PERCENT:-80}"
THREADS="${THREADS:-}"
DURATION="${DURATION:-600}"
ROUTE_LABEL="${ROUTE_LABEL:-app.kubernetes.io/instance=dotnet5-application-dev}"

if [[ -z "${ROUTE:-}" ]]; then
  HOST=$(oc get route -n "$NS" -l "$ROUTE_LABEL" -o jsonpath='{.items[0].spec.host}' 2>/dev/null || true)
  if [[ -z "$HOST" ]]; then
    echo "Erro: não foi possível obter a Route. Defina ROUTE=https://..."
    exit 1
  fi
  ROUTE="https://${HOST}"
fi

TARGET="${ROUTE%/}"

resolve_mode() {
  case "${1:-}" in
    cpu|memory|restore-readiness|restore-liveness|verify-readiness-drain) echo "$1" ;;
    "") echo "memory" ;;
    *) echo "unknown" ;;
  esac
}

MODE=$(resolve_mode "${1:-}")

run_memory_stress() {
  echo "Target:   $TARGET"
  echo "Memória:  ${MB}MB por ${DURATION}s"
  echo ""

  echo "=== Status antes ==="
  curl -sk "$TARGET/api/stress/status" || echo "(stress desabilitado — habilite com: oc set env deployment/$DEPLOY StressTest__Enabled=true -n $NS)"
  echo ""

  echo "=== Iniciando stress de memória ==="
  curl -sk -X POST "$TARGET/api/stress/memory" \
    -H "Content-Type: application/json" \
    -d "{\"megabytes\":${MB},\"durationSeconds\":${DURATION}}"
  echo ""

  echo ""
  echo "Monitorar HPA:"
  echo "  watch -n 5 'oc get hpa $DEPLOY -n $NS; oc adm top pods -n $NS'"
  echo ""
  echo "Parar manualmente:"
  echo "  curl -sk -X DELETE $TARGET/api/stress/memory"
}

run_cpu_stress() {
  local payload
  if [[ -n "$THREADS" ]]; then
    payload="{\"percentCpu\":${PERCENT},\"threads\":${THREADS},\"durationSeconds\":${DURATION}}"
    echo "Target:   $TARGET"
    echo "CPU:      ${THREADS} threads por ${DURATION}s (percentCpu=${PERCENT} ignorado quando threads definido)"
  else
    payload="{\"percentCpu\":${PERCENT},\"durationSeconds\":${DURATION}}"
    echo "Target:   $TARGET"
    echo "CPU:      ${PERCENT}% dos cores lógicos por ${DURATION}s"
  fi
  echo ""

  echo "=== Status antes ==="
  curl -sk "$TARGET/api/stress/status" || echo "(stress desabilitado — habilite com: oc set env deployment/$DEPLOY StressTest__Enabled=true -n $NS)"
  echo ""

  echo "=== Iniciando stress de CPU ==="
  curl -sk -X POST "$TARGET/api/stress/cpu" \
    -H "Content-Type: application/json" \
    -d "$payload"
  echo ""

  echo ""
  echo "Monitorar HPA:"
  echo "  watch -n 5 'oc get hpa $DEPLOY -n $NS; oc adm top pods -n $NS'"
  echo ""
  echo "Parar manualmente:"
  echo "  curl -sk -X DELETE $TARGET/api/stress/cpu"
}

restore_probe() {
  local kind="$1"
  local endpoint="$TARGET/api/stress/${kind}/degrade"
  local replicas

  replicas=$(oc get deployment "$DEPLOY" -n "$NS" -o jsonpath='{.spec.replicas}' 2>/dev/null || echo "2")
  local attempts=$((replicas * 5))

  echo ""
  echo "=== Restaurando ${kind} em todos os pods (~${replicas} réplicas, ${attempts} tentativas) ==="

  for _ in $(seq 1 "$attempts"); do
    response=$(curl -sk -X DELETE "$endpoint")
    pod=$(echo "$response" | grep -o '"pod":"[^"]*"' | cut -d'"' -f4 || true)
    degraded=$(echo "$response" | grep -o '"'"${kind}Degraded"':[^,}]*' | cut -d: -f2 || true)
    echo "  pod=${pod:-?} ${kind}Degraded=${degraded:-?}"
    sleep 0.3
  done

  echo ""
  echo "Pods:"
  oc get pods -n "$NS" -l "$APP_LABEL" 2>/dev/null || oc get pods -n "$NS" | grep "$DEPLOY" || true
}

verify_readiness_drain() {
  echo "=== Verificar: readiness remove pod do balanceamento ==="
  echo "Namespace: $NS  Deployment: $DEPLOY"
  echo ""

  pods=($(oc get pods -n "$NS" -l "$APP_LABEL" -o jsonpath='{.items[*].metadata.name}' 2>/dev/null || true))
  if [[ ${#pods[@]} -lt 2 ]]; then
    echo "Precisa de 2+ pods. Escale: oc scale deployment/$DEPLOY --replicas=2 -n $NS"
    exit 1
  fi

  victim="${pods[0]}"
  healthy="${pods[1]}"

  echo "Pod a degradar (via exec, sem Route): $victim"
  echo "Pod saudável esperado no tráfego:     $healthy"
  echo ""

  oc exec -n "$NS" "$victim" -- curl -s localhost:8080/api/stress/readiness/degrade >/dev/null
  echo "Aguardando pod ficar Not Ready (~15s: 3 falhas × 5s)..."
  sleep 18

  echo ""
  echo "=== Pods ==="
  oc get pods -n "$NS" -l "$APP_LABEL" -o wide

  echo ""
  echo "=== Endpoints do Service (só IPs Ready entram no balanceamento) ==="
  oc get endpoints "$DEPLOY" -n "$NS" -o wide 2>/dev/null || oc get endpoints -n "$NS" | grep "$DEPLOY"

  echo ""
  echo "=== 30 requests via Route — deve responder só o pod saudável ==="
  pods_seen=$(mktemp)
  for _ in $(seq 1 30); do
    curl -sk "$TARGET/api/stress/status" | grep -o '"pod":"[^"]*"' | cut -d'"' -f4 >> "$pods_seen" || true
  done
  sort "$pods_seen" | uniq -c | while read -r count pod; do
    echo "  $pod → $count requests"
  done
  rm -f "$pods_seen"

  echo ""
  echo "Restaurar: oc exec -n $NS $victim -- curl -s -X DELETE localhost:8080/api/stress/readiness/degrade"
  echo "Ou:        ./CrudApp/stress-example.sh restore-readiness"
}

case "$MODE" in
  memory) run_memory_stress ;;
  cpu) run_cpu_stress ;;
  restore-readiness) restore_probe readiness ;;
  restore-liveness) restore_probe liveness ;;
  verify-readiness-drain) verify_readiness_drain ;;
  *)
    echo "Uso: $0 [memory|cpu|restore-readiness|restore-liveness|verify-readiness-drain]"
    exit 1
    ;;
esac

#!/usr/bin/env bash
# Stress test via curl — não precisa de dotnet local.
#
# OpenShift (mercantil-dev):
#   ./CrudApp/stress-example.sh
#
# Parâmetros:
#   NS=mercantil-dev MB=120 DURATION=600 ./CrudApp/stress-example.sh
#   ROUTE=https://minha-route.apps.cluster.com MB=120 ./CrudApp/stress-example.sh

set -euo pipefail

NS="${NS:-mercantil-dev}"
MB="${MB:-120}"
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

echo "Target:   $TARGET"
echo "Memória:  ${MB}MB por ${DURATION}s"
echo ""

echo "=== Status antes ==="
curl -sk "$TARGET/api/stress/status" || echo "(stress desabilitado — habilite com: oc set env deployment/my-dotnet5-crud StressTest__Enabled=true -n $NS)"
echo ""

echo "=== Iniciando stress de memória ==="
curl -sk -X POST "$TARGET/api/stress/memory" \
  -H "Content-Type: application/json" \
  -d "{\"megabytes\":${MB},\"durationSeconds\":${DURATION}}"
echo ""

echo ""
echo "Monitorar HPA:"
echo "  watch -n 5 'oc get hpa my-dotnet5-crud -n $NS; oc adm top pods -n $NS'"
echo ""
echo "Parar manualmente:"
echo "  curl -sk -X DELETE $TARGET/api/stress/memory"

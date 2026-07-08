#!/usr/bin/env bash
# Dispara stress de memória via API (requer StressTest__Enabled=true no pod).
#
# Uso local:
#   ./stress-example.sh
#
# OpenShift:
#   ROUTE=https://sua-route.apps.cluster.com ./stress-example.sh

set -euo pipefail

NS=dotnet-builders
TARGET="${ROUTE:-http://localhost:5000}"
TARGET="${TARGET%/}"

echo "Target: $TARGET"
echo "=== Status antes ==="
curl -s "$TARGET/api/stress/status" || echo "(stress desabilitado ou indisponível)"
echo ""

echo "=== Iniciando stress de memória (480MB / 300s) ==="
curl -s -X POST "$TARGET/api/stress/memory" \
  -H "Content-Type: application/json" \
  -d '{"megabytes":480,"durationSeconds":300}'
echo ""

echo ""
echo "Monitorar:"
echo "  oc get hpa -n $NS"
echo "  oc adm top pods -n $NS"
echo ""
echo "Parar manualmente:"
echo "  curl -X DELETE $TARGET/api/stress/memory"

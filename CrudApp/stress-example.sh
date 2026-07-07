#!/usr/bin/env bash
# Teste de stress de memória para validar HPA no namespace dotnet-builders.
#
# Scale down demora porque:
#   1. HPA aguarda 5 min (stabilizationWindowSeconds) com memória abaixo de 65%
#   2. O arquivo /tmp/stress permanece no pod e mantém page cache alto
#   3. Scale down remove no máximo 1 pod por minuto
#
# Após parar o stress, aguarde ~6 min e monitore:
#   watch -n 10 'oc get hpa -n dotnet-builders; oc adm top pods -n dotnet-builders'

set -euo pipefail

NS=dotnet-builders
oc project "$NS"

POD=$(oc get pods -l app=dotnet5-crud -o jsonpath='{.items[0].metadata.name}' -n "$NS")

echo "Pod: $POD"
echo "=== Memória antes ==="
oc adm top pod "$POD" -n "$NS"

echo "=== Alocando 480MB (dd + sleep 300s) ==="
oc exec "$POD" -n "$NS" -- bash -c 'dd if=/dev/zero of=/tmp/stress bs=1M count=480 2>/dev/null; sleep 300; rm -f /tmp/stress'

echo "=== Memória depois (pode demorar a cair — page cache) ==="
sleep 10
oc adm top pod "$POD" -n "$NS"
oc get hpa -n "$NS"

echo ""
echo "Scale down: aguarde ~5 min abaixo de 65% + 1 pod/min. Monitore com:"
echo "  watch -n 10 'oc get hpa -n $NS; oc adm top pods -n $NS'"

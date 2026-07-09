# CrudApp — API CRUD em .NET 5

API REST simples para gerenciar produtos (Create, Read, Update, Delete).

## Requisitos

- [.NET 5 SDK](https://dotnet.microsoft.com/download/dotnet/5.0)

## Executar

```bash
cd CrudApp
dotnet run
```

A API fica disponível em `http://localhost:5000`.

## Frontend

A interface web é servida pela própria aplicação. Após subir o projeto, acesse:

```
http://localhost:5000
```

No OpenShift, acesse a URL da Route (porta 8080). O frontend consome a API na mesma origem — sem necessidade de CORS.

Funcionalidades da UI:

- Listar produtos
- Criar novo produto
- Editar produto existente
- Excluir com confirmação
- Atualizar lista manualmente

## Endpoints

| Método | Rota | Descrição |
|--------|------|-----------|
| GET | `/api/products` | Lista todos os produtos |
| GET | `/api/products/{id}` | Busca produto por ID |
| POST | `/api/products` | Cria um produto |
| PUT | `/api/products/{id}` | Atualiza um produto |
| DELETE | `/api/products/{id}` | Remove um produto |
| GET | `/health/live` | Liveness probe (OpenShift/Kubernetes) |
| GET | `/health/ready` | Readiness probe (OpenShift/Kubernetes) |
| GET | `/api/stress/status` | Status do stress (se habilitado) |
| POST | `/api/stress/memory` | Aloca memória para teste de HPA |
| DELETE | `/api/stress/memory` | Libera memória alocada |
| GET/POST | `/api/stress/liveness/degrade` | Simula falha na liveness (teste) |
| DELETE | `/api/stress/liveness/degrade` | Restaura liveness após teste |
| GET/POST | `/api/stress/readiness/degrade` | Simula falha na readiness (teste) |
| DELETE | `/api/stress/readiness/degrade` | Restaura readiness após teste |

## Guia de testes no OpenShift

Este guia cobre três tipos de teste operacional da aplicação no cluster. Todos os endpoints `/api/stress/*` exigem **`StressTest:Enabled=true`** — fora disso retornam `404`.

### Pré-requisitos comuns

Defina variáveis no terminal (ajuste se necessário):

```bash
export NS=mercantil-dev
export DEPLOY=my-dotnet5-crud
export HPA=$DEPLOY

export ROUTE="http://$(oc get route -n $NS -l app.kubernetes.io/instance=dotnet5-application-dev -o jsonpath='{.items[0].spec.host}')"
echo "ROUTE=$ROUTE"
```

Habilite os endpoints de teste:

```bash
oc set env deployment/$DEPLOY StressTest__Enabled=true -n $NS
```

Confirme que estão ativos:

```bash
curl -sk "$ROUTE/api/stress/status"
# {"enabled":true,"memoryActive":false,"livenessDegraded":false,"readinessDegraded":false,...}
```

Para desabilitar após os testes:

```bash
oc set env deployment/$DEPLOY StressTest__Enabled- -n $NS
```

---

### 1. StressTest (HPA — memória e carga de API)

Use para validar **scale up/down** do HorizontalPodAutoscaler.

#### Probes e HPA no Deployment (referência)

O HPA escala com base em CPU e memória. Exemplo de targets:

| Métrica | Target típico |
|---------|---------------|
| CPU | 60% do `request` |
| Memória | 70% do `request` |

Com `memory request: 128Mi`, **70% ≈ 90Mi** de uso médio dispara scale up.

#### Opção A — script bash (recomendado, sem dotnet local)

```bash
chmod +x CrudApp/stress-example.sh

# Padrão: 120MB por 600s
./CrudApp/stress-example.sh

# Teste rápido (1 minuto)
MB=120 DURATION=60 ./CrudApp/stress-example.sh
```

#### Opção B — curl manual (stress de memória)

```bash
# Iniciar — aloca RAM real no pod (ideal para HPA)
curl -sk -X POST "$ROUTE/api/stress/memory" \
  -H "Content-Type: application/json" \
  -d '{"megabytes":120,"durationSeconds":60}'

# Status
curl -sk "$ROUTE/api/stress/status"

# Parar antes do timeout
curl -sk -X DELETE "$ROUTE/api/stress/memory"
```

#### Opção C — subaplicação `CrudApp.StressTest` (CLI .NET)

Requer SDK .NET instalado localmente:

```bash
# Stress de memória
dotnet run --project CrudApp.StressTest -- \
  -t "$ROUTE" -m memory --mb 120 -d 60

# Stress de API (muitos POSTs paralelos — CPU/carga CRUD)
dotnet run --project CrudApp.StressTest -- \
  -t "$ROUTE" -m api -r 2000 -c 50

# Memória + API em sequência
dotnet run --project CrudApp.StressTest -- \
  -t "$ROUTE" -m all --mb 120 -d 60 -r 2000 -c 50
```

#### Monitorar o HPA

```bash
watch -n 10 'echo "=== HPA ==="; oc get hpa $HPA -n $NS; echo; echo "=== PODS ==="; oc get pods -n $NS | grep $DEPLOY; echo; echo "=== MEMÓRIA ==="; oc adm top pods -n $NS | grep $DEPLOY'
```

**O que esperar:**

| Fase | Sinal |
|------|-------|
| Scale up | `TARGETS` memória > 70%; `REPLICAS` sobe |
| Scale down | Métricas caem; após ~5 min `REPLICAS` volta a 1 |
| ArgoCD | Use `ignoreDifferences` em `/spec/replicas` para evitar conflito com HPA |

---

### 2. Simular falha de Liveness

Use para provar que o **kubelet reinicia o pod** quando a aplicação deixa de responder como viva.

#### Probe no Deployment

```yaml
livenessProbe:
  httpGet:
    path: /health/live
    port: 8080
  initialDelaySeconds: 30
  periodSeconds: 10
  timeoutSeconds: 3
  failureThreshold: 3
```

Com esses valores, **3 falhas × 10s ≈ 30s** até o restart.

#### Passo a passo

```bash
# 1. Confirmar saudável
curl -sk "$ROUTE/health/live" -w "\nHTTP %{http_code}\n"
# HTTP 200 — body: Healthy

# 2. Degradar a liveness
curl -sk "$ROUTE/api/stress/liveness/degrade"
# GET funciona sem body; POST requer: -d ''

# 3. Confirmar falha
curl -sk "$ROUTE/health/live" -w "\nHTTP %{http_code}\n"
# HTTP 503 — Unhealthy

# 4. Monitorar restart do pod (~30-40s)
watch -n 5 "oc get pods -n $NS | grep $DEPLOY"
```

**O que esperar:**

| Evento | Resultado |
|--------|-----------|
| `/health/live` degradado | HTTP **503** |
| Após ~30s | Coluna `RESTARTS` incrementa; pod recriado |
| Novo pod | Liveness saudável (estado em memória se perde) |

#### Restaurar manualmente (opcional, antes do restart)

```bash
curl -sk -X DELETE "$ROUTE/api/stress/liveness/degrade"
curl -sk "$ROUTE/health/live" -w "\nHTTP %{http_code}\n"   # 200
```

---

### 3. Simular falha de Readiness

Use para provar que o pod **sai do Service** (deixa de receber tráfego) **sem ser reiniciado**.

#### Probe no Deployment

```yaml
readinessProbe:
  httpGet:
    path: /health/ready
    port: 8080
  initialDelaySeconds: 15
  periodSeconds: 5
  timeoutSeconds: 3
  failureThreshold: 3
```

Com esses valores, **3 falhas × 5s ≈ 15s** até ficar Not Ready.

#### Passo a passo

```bash
# 1. Confirmar saudável
curl -sk "$ROUTE/health/ready" -w "\nHTTP %{http_code}\n"
# HTTP 200

# 2. Degradar a readiness
curl -sk "$ROUTE/api/stress/readiness/degrade"

# 3. Confirmar falha
curl -sk "$ROUTE/health/ready" -w "\nHTTP %{http_code}\n"
# HTTP 503

# 4. Pod continua Running, mas Not Ready (~15s)
watch -n 5 "oc get pods -n $NS | grep $DEPLOY"
# Exemplo: 0/1 Running  (ou 1/1 → 0/1 Ready)
```

**O que esperar:**

| Evento | Resultado |
|--------|-----------|
| `/health/ready` degradado | HTTP **503** |
| Pod | **Running**, porém **Not Ready** |
| Tráfego | Route deixa de enviar requests ao pod |
| `RESTARTS` | **Não** incrementa (diferente da liveness) |

#### Restaurar

```bash
curl -sk -X DELETE "$ROUTE/api/stress/readiness/degrade"
curl -sk "$ROUTE/health/ready" -w "\nHTTP %{http_code}\n"   # 200

# Pod volta a Ready e recebe tráfego
oc get pods -n $NS | grep $DEPLOY
# 1/1 Running
```

---

### Comparativo rápido

| Teste | Endpoint degradado | Efeito no pod | Reinicia? |
|-------|-------------------|---------------|-----------|
| **StressTest (memória)** | — | HPA scale up/down | Não |
| **Liveness** | `/health/live` → 503 | Kubelet mata e recria | **Sim** (~30s) |
| **Readiness** | `/health/ready` → 503 | Remove do Service | **Não** |

## Health checks (OpenShift)

A aplicação expõe dois endpoints para probes:

- **`/health/live`** — indica que o processo está vivo (LivenessProbe)
- **`/health/ready`** — indica que a aplicação está pronta para receber tráfego (ReadinessProbe)

Exemplo de configuração no Deployment está em `openshift/deployment.yaml`. Para aplicar as probes em um Deployment existente:

```bash
oc set probe deployment/crudapp -n dotnet-builders --liveness --get-url=http://:8080/health/live --initial-delay-seconds=15
oc set probe deployment/crudapp -n dotnet-builders --readiness --get-url=http://:8080/health/ready --initial-delay-seconds=5
```

No container S2I .NET, a aplicação escuta na porta **8080**.

## HPA (autoscaling)

Escala quando o uso médio de memória dos pods ultrapassa **65%** do `memory request` (128Mi em `openshift/deployment.yaml`).

Aplicar:

```bash
oc apply -f openshift/hpa.yaml -n mercantil-dev
oc get hpa my-dotnet5-crud -n mercantil-dev
oc adm top pods -n mercantil-dev
```

Para testes de stress e scale, veja a seção **Guia de testes no OpenShift** acima.

## ArgoCD + HPA (OutOfSync em replicas)

O HPA altera `deployment.spec.replicas` no cluster; o Git permanece com `replicas: 1` → ArgoCD fica **OutOfSync**. Isso é normal sem `ignoreDifferences`.

No **Application** do ArgoCD (repo GitOps):

```yaml
spec:
  ignoreDifferences:
    - group: apps
      kind: Deployment
      jsonPointers:
        - /spec/replicas
```

Para **destravar scale down agora** (HPA sobrescreve `oc scale`):

```bash
oc delete hpa my-dotnet5-crud -n mercantil-dev
oc scale deployment/my-dotnet5-crud --replicas=1 -n mercantil-dev
oc get rs -n mercantil-dev | grep my-dotnet5-crud
# Reaplicar HPA via ArgoCD sync
```

## Exemplos com curl

```bash
# Criar
curl -X POST http://localhost:5000/api/products \
  -H "Content-Type: application/json" \
  -d '{"name":"Notebook","price":3500.00,"quantity":10}'

# Listar
curl http://localhost:5000/api/products

# Buscar por ID
curl http://localhost:5000/api/products/1

# Atualizar
curl -X PUT http://localhost:5000/api/products/1 \
  -H "Content-Type: application/json" \
  -d '{"name":"Notebook Pro","price":4200.00,"quantity":5}'

# Remover
curl -X DELETE http://localhost:5000/api/products/1
```

Os dados são armazenados em memória e são perdidos ao reiniciar a aplicação.

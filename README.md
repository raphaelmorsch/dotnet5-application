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
| POST | `/api/stress/cpu` | Consome CPU para teste de HPA |
| DELETE | `/api/stress/cpu` | Para o stress de CPU |
| GET/POST | `/api/stress/liveness/degrade` | Simula falha na liveness (teste) |
| DELETE | `/api/stress/liveness/degrade` | Restaura liveness após teste |
| GET/POST | `/api/stress/readiness/degrade` | Simula falha na readiness (teste) |
| DELETE | `/api/stress/readiness/degrade` | Restaura readiness após teste |

## Guia de testes no OpenShift

Este guia cobre quatro tipos de teste operacional da aplicação no cluster:

| # | Tipo | Objetivo | Escopo |
|---|------|----------|--------|
| 1 | **Stress in-pod** (memória / CPU) | Disparar HPA por métrica específica | **Um pod** por request (Route load balance) |
| 2 | **Carga de requests** (CRUD / Ddosify) | Simular tráfego real em toda a aplicação | **Todos os pods** via Route |
| 3 | **Liveness** | Provar restart automático pelo kubelet | Por pod |
| 4 | **Readiness** | Provar remoção do balanceamento sem restart | Por pod |

Todos os endpoints `/api/stress/*` exigem **`StressTest:Enabled=true`** — fora disso retornam `404`. Os endpoints `/api/products` **não** exigem essa flag.

### Pré-requisitos comuns

Defina variáveis no terminal (ajuste se necessário):

```bash
export NS=mercantil-dev
export DEPLOY=my-dotnet5-crud
export HPA=$DEPLOY

export ROUTE="https://$(oc get route -n $NS -l app.kubernetes.io/instance=dotnet5-application-dev -o jsonpath='{.items[0].spec.host}')"
echo "ROUTE=$ROUTE"
```

Habilite os endpoints de teste:

```bash
oc set env deployment/$DEPLOY StressTest__Enabled=true -n $NS
```

Confirme que estão ativos:

```bash
curl -sk "$ROUTE/api/stress/status"
# {"enabled":true,"pod":"...","memoryActive":false,"cpuActive":false,"cpuPercent":0,...}
```

Para desabilitar após os testes:

```bash
oc set env deployment/$DEPLOY StressTest__Enabled- -n $NS
```

---

### 1. Stress in-pod (HPA — memória e CPU)

Use para validar **scale up/down** do HorizontalPodAutoscaler com consumo controlado **dentro de um pod**.

> **Importante:** `POST /api/stress/memory` e `POST /api/stress/cpu` afetam **apenas o pod que atende a requisição**. Para stressar um pod específico, use `oc exec` (veja exemplos abaixo). Para carga em **todos os pods**, use a seção [2. Carga de requests](#2-carga-de-requests-crud--ddosify).

#### Probes e HPA no Deployment (referência)

O HPA escala com base em CPU e memória. Exemplo de targets:

| Métrica | Target típico |
|---------|---------------|
| CPU | 60% do `request` |
| Memória | 70% do `request` |

Com `memory request: 128Mi`, **70% ≈ 90Mi** de uso médio dispara scale up.

Com `cpu request: 100m`, **60% ≈ 60m** de uso médio dispara scale up.

#### Opção A — script bash (recomendado, sem dotnet local)

```bash
chmod +x CrudApp/stress-example.sh

# Memória — padrão: 120MB por 600s
./CrudApp/stress-example.sh

# Memória — teste rápido (1 minuto)
MB=120 DURATION=60 ./CrudApp/stress-example.sh

# CPU — 80% dos cores lógicos por 120s
PERCENT=80 DURATION=120 ./CrudApp/stress-example.sh cpu

# CPU — 2 threads fixas por 60s
THREADS=2 DURATION=60 ./CrudApp/stress-example.sh cpu
```

Outros subcomandos do script:

| Subcomando | Descrição |
|------------|-----------|
| `./CrudApp/stress-example.sh` | Stress de memória (padrão) |
| `./CrudApp/stress-example.sh cpu` | Stress de CPU |
| `./CrudApp/stress-example.sh restore-readiness` | Restaura readiness em todos os pods |
| `./CrudApp/stress-example.sh restore-liveness` | Restaura liveness em todos os pods |
| `./CrudApp/stress-example.sh verify-readiness-drain` | Prova remoção do balanceamento (2+ pods) |

#### Opção B — curl manual (stress de memória)

```bash
# Iniciar — aloca RAM real no pod (ideal para HPA por memória)
curl -sk -X POST "$ROUTE/api/stress/memory" \
  -H "Content-Type: application/json" \
  -d '{"megabytes":120,"durationSeconds":60}'

# Status
curl -sk "$ROUTE/api/stress/status"

# Parar antes do timeout
curl -sk -X DELETE "$ROUTE/api/stress/memory"
```

#### Opção B2 — curl manual (stress de CPU)

```bash
# Iniciar — consome CPU dentro do pod (ideal para HPA por CPU)
curl -sk -X POST "$ROUTE/api/stress/cpu" \
  -H "Content-Type: application/json" \
  -d '{"percentCpu":80,"durationSeconds":120}'

# Ou threads explícitas (sobrescreve o cálculo por percentCpu)
curl -sk -X POST "$ROUTE/api/stress/cpu" \
  -H "Content-Type: application/json" \
  -d '{"percentCpu":80,"threads":2,"durationSeconds":120}'

# Status
curl -sk "$ROUTE/api/stress/status"

# Parar antes do timeout
curl -sk -X DELETE "$ROUTE/api/stress/cpu"
```

| Parâmetro | Valores | Descrição |
|-----------|---------|-----------|
| `percentCpu` | 1–100 | % dos cores lógicos do pod |
| `threads` | 1–64 (opcional) | Threads busy-loop explícitas |
| `durationSeconds` | 1–3600 | Para automaticamente após o tempo |

Stress em **pod específico** (sem depender do load balance da Route):

```bash
POD=$(oc get pods -n $NS -l app=$DEPLOY -o jsonpath='{.items[0].metadata.name}')

oc exec -n $NS "$POD" -- curl -s -X POST localhost:8080/api/stress/cpu \
  -H "Content-Type: application/json" \
  -d '{"percentCpu":80,"durationSeconds":120}'
```

#### Opção C — subaplicação `CrudApp.StressTest` (CLI .NET)

Requer SDK .NET instalado localmente:

```bash
# Stress de memória
dotnet run --project CrudApp.StressTest -- \
  -t "$ROUTE" -m memory --mb 120 -d 60

# Stress de CPU (dentro do pod)
dotnet run --project CrudApp.StressTest -- \
  -t "$ROUTE" -m cpu --percent-cpu 80 -d 120

# Stress de API (muitos POSTs paralelos — carga CRUD indireta)
dotnet run --project CrudApp.StressTest -- \
  -t "$ROUTE" -m api -r 2000 -c 50

# Memória + API em sequência
dotnet run --project CrudApp.StressTest -- \
  -t "$ROUTE" -m all --mb 120 -d 60 -r 2000 -c 50
```

#### Monitorar o HPA

```bash
watch -n 10 'echo "=== HPA ==="; oc get hpa $HPA -n $NS; echo; echo "=== PODS ==="; oc get pods -n $NS | grep $DEPLOY; echo; echo "=== RECURSOS ==="; oc adm top pods -n $NS | grep $DEPLOY'
```

**O que esperar:**

| Fase | Sinal |
|------|-------|
| Scale up (memória) | `TARGETS` memória > 70%; `REPLICAS` sobe |
| Scale up (CPU) | `TARGETS` CPU > 60%; `REPLICAS` sobe |
| Scale down | Métricas caem; após ~5 min `REPLICAS` volta a 1 |
| ArgoCD | Use `ignoreDifferences` em `/spec/replicas` para evitar conflito com HPA |

---

### 2. Carga de requests (CRUD / Ddosify)

Use para simular **tráfego HTTP real** distribuído pela **Route** entre **todos os pods Ready**. Não exige `StressTest:Enabled`.

Ideal para validar comportamento da aplicação sob carga (latência, throughput, scale por CPU/memória induzida pelo tráfego).

#### curl — cadastrar produto

```bash
curl -sk -X POST "$ROUTE/api/products" \
  -H "Content-Type: application/json" \
  -d '{"name":"Produto Load Test","price":19.99,"quantity":10}'
# HTTP 201 Created → {"id":1,"name":"Produto Load Test","price":19.99,"quantity":10}
```

#### Outros endpoints úteis para carga

```bash
# Listar (GET — não cresce memória)
curl -sk "$ROUTE/api/products"

# Buscar por ID
curl -sk "$ROUTE/api/products/1"

# Atualizar
curl -sk -X PUT "$ROUTE/api/products/1" \
  -H "Content-Type: application/json" \
  -d '{"name":"Produto Atualizado","price":29.99,"quantity":5}'

# Remover
curl -sk -X DELETE "$ROUTE/api/products/1"
```

#### Configuração no Ddosify (exemplo)

| Campo | Valor |
|-------|-------|
| **Method** | `POST` |
| **URL** | `https://SUA-ROUTE.apps.cluster.com/api/products` |
| **Header** | `Content-Type: application/json` |
| **Body** | `{"name":"Produto Load Test","price":19.99,"quantity":10}` |
| **Target RPS** | `200` (ajuste conforme necessário) |

Se o Ddosify suportar variáveis dinâmicas no body:

```json
{"name":"load-{{random}}","price":19.99,"quantity":1}
```

#### Mix recomendado (carga realista)

| Cenário | % | Método | URL |
|---------|---|--------|-----|
| Criar produto | 30% | POST | `/api/products` |
| Listar produtos | 50% | GET | `/api/products` |
| Buscar por ID | 20% | GET | `/api/products/1` |

#### Validar distribuição entre pods

Com `StressTest:Enabled=true`, confira quais pods respondem:

```bash
for i in $(seq 1 50); do
  curl -sk "$ROUTE/api/stress/status" | grep -o '"pod":"[^"]*"'
done | sort | uniq -c
```

Monitore recursos de todos os pods durante o teste:

```bash
watch -n 2 'oc get pods -n $NS -l app=$DEPLOY; echo; oc adm top pods -n $NS | grep $DEPLOY'
```

#### Atenção: POST contínuo enche memória

Cada `POST /api/products` persiste um produto **em memória** (repositório in-memory). A **200 req/s** de criação gera ~12.000 produtos/minuto — isso pode disparar scale por **memória** além de CPU. Para testes longos, prefira mix com GET ou carga só de leitura.

---

### 3. Simular falha de Liveness

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

Com vários pods, repita o `DELETE` ou use `./CrudApp/stress-example.sh restore-liveness`.

```bash
curl -sk -X DELETE "$ROUTE/api/stress/liveness/degrade"
curl -sk "$ROUTE/health/live" -w "\nHTTP %{http_code}\n"   # 200
```

---

### 4. Simular falha de Readiness

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
| Tráfego | **Service/Route param de enviar** requests ao pod (só pods Ready no Endpoints) |
| `RESTARTS` | **Não** incrementa (diferente da liveness) |

#### Provar remoção do balanceamento (2 pods)

Degrade **um pod específico** via `oc exec` (não via Route — senão você não controla qual pod recebe o degrade):

```bash
# Helper automatizado (30 requests + Endpoints)
./CrudApp/stress-example.sh verify-readiness-drain

# Manual
POD=$(oc get pods -n $NS -l app=$DEPLOY -o jsonpath='{.items[0].metadata.name}')
oc exec -n $NS $POD -- curl -s localhost:8080/api/stress/readiness/degrade

# Endpoints: IP do pod Not Ready some da lista
oc get endpoints my-dotnet5-crud -n $NS -o wide

# Route só balanceia entre pods Ready
for i in $(seq 1 20); do curl -sk "$ROUTE/api/stress/status" | grep -o '"pod":"[^"]*"'; done
# → só aparece o nome do pod saudável
```

#### Restaurar

O estado de degrade é **por pod** (memória local). A Route distribui requests — um único `DELETE` restaura **apenas o pod que atendeu** aquela requisição.

```bash
# Ver qual pod respondeu (campo "pod" no JSON)
curl -sk -X DELETE "$ROUTE/api/stress/readiness/degrade"

# Com 2+ pods: repita até todos estarem Ready
for i in $(seq 1 10); do
  curl -sk -X DELETE "$ROUTE/api/stress/readiness/degrade"
  sleep 0.5
done

# Ou use o helper do script (lê réplicas do Deployment)
./CrudApp/stress-example.sh restore-readiness

# Conferir status por pod (cada chamada pode cair em pod diferente)
curl -sk "$ROUTE/api/stress/status"

# Alternativa: degradar todos de uma vez via env (rollout recria pods com a flag)
oc set env deployment/my-dotnet5-crud StressTest__ReadinessDegraded=true -n $NS
oc set env deployment/my-dotnet5-crud StressTest__ReadinessDegraded- -n $NS

curl -sk "$ROUTE/health/ready" -w "\nHTTP %{http_code}\n"   # 200

# Pod volta a Ready e recebe tráfego
oc get pods -n $NS | grep $DEPLOY
# 1/1 Running
```

---

### Comparativo rápido

| Teste | Como disparar | Escopo | Efeito | Reinicia pod? |
|-------|---------------|--------|--------|---------------|
| **Stress memória** | `POST /api/stress/memory` | 1 pod/request | HPA scale up/down | Não |
| **Stress CPU** | `POST /api/stress/cpu` | 1 pod/request | HPA scale up/down | Não |
| **Carga CRUD / Ddosify** | `POST/GET /api/products` | Todos os pods (Route) | CPU + memória por tráfego real | Não |
| **Liveness** | `/health/live` → 503 | 1 pod/request | Kubelet mata e recria | **Sim** (~30s) |
| **Readiness** | `/health/ready` → 503 | 1 pod/request | Remove do Service/Route | **Não** |

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

Escala quando o uso médio de **CPU** ou **memória** dos pods ultrapassa os targets configurados (ex.: CPU 60%, memória 70% do `request`).

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

### Local

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

### OpenShift (via Route)

```bash
export ROUTE="https://$(oc get route -n mercantil-dev -l app.kubernetes.io/instance=dotnet5-application-dev -o jsonpath='{.items[0].spec.host}')"

curl -sk -X POST "$ROUTE/api/products" \
  -H "Content-Type: application/json" \
  -d '{"name":"Notebook","price":3500.00,"quantity":10}'

curl -sk "$ROUTE/api/products"
```

Os dados são armazenados em memória e são perdidos ao reiniciar a aplicação.

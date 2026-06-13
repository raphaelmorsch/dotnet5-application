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

Namespace usado nos exemplos: **`dotnet-builders`**. Ajuste o nome do Deployment se for diferente (ex.: `dotnet-1`):

```bash
oc get deployment -n dotnet-builders
```

## HPA (autoscaling por memória)

Escala quando o uso médio de memória dos pods ultrapassa **65%** do `memory request` (128Mi em `openshift/deployment.yaml`).

Aplicar:

```bash
oc apply -f openshift/deployment.yaml -n dotnet-builders
oc apply -f openshift/hpa.yaml -n dotnet-builders
```

Verificar:

```bash
oc get hpa -n dotnet-builders
oc describe hpa crudapp -n dotnet-builders
oc adm top pods -n dotnet-builders
```

## Teste de stress do HPA

### 1. Confirmar métricas

```bash
NS=dotnet-builders

oc get hpa -n "$NS"
oc adm top pods -n "$NS"
oc describe hpa crudapp -n "$NS"
```

O HPA deve exibir `TARGETS` com percentual (ex.: `45%/65%`). Se aparecer `<unknown>`, as métricas ainda não estão disponíveis.

### 2. Monitorar em tempo real

```bash
watch -n 5 'echo "=== HPA ==="; oc get hpa -n dotnet-builders; echo; echo "=== PODS ==="; oc get pods -n dotnet-builders; echo; echo "=== MEMÓRIA ==="; oc adm top pods -n dotnet-builders'
```

O HPA sobe **+1 pod por minuto** após passar de 65% — aguarde **2–5 minutos** após a memória subir.

### 3. Gerar carga (produtos em memória)

```bash
ROUTE="https://SUA-ROUTE-AQUI"

for i in $(seq 1 3000); do
  curl -s -X POST "$ROUTE/api/products" \
    -H "Content-Type: application/json" \
    -d "{\"name\":\"$(head -c 20000 /dev/zero | tr '\0' 'x')\",\"price\":1,\"quantity\":1}" &
  if (( i % 100 == 0 )); then echo "Criados: $i"; fi
done
wait
```

### 4. Validar escala

```bash
oc adm top pods -n dotnet-builders
oc get hpa -n dotnet-builders
oc get pods -n dotnet-builders
oc describe hpa crudapp -n dotnet-builders | tail -20
```

**65% de 128Mi ≈ 83Mi** — acima disso, o HPA deve aumentar as réplicas.

### 5. Limpeza

```bash
oc delete pod -l app=crudapp -n dotnet-builders
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

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
oc set probe deployment/crudapp --liveness --get-url=http://:8080/health/live --initial-delay-seconds=15
oc set probe deployment/crudapp --readiness --get-url=http://:8080/health/ready --initial-delay-seconds=5
```

No container S2I .NET, a aplicação escuta na porta **8080**.

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

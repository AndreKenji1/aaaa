# Testes BDD, API e contratos do HydroGuard

## Execução local

Instale o SDK .NET 8 e execute na raiz do repositório:

```bash
bash scripts/test.sh
```

O script pode ser chamado de qualquer diretório, restaura os pacotes NuGet, compila a solução em Release e executa a suíte xUnit, incluindo os cenários Gherkin gerados pelo Reqnroll. Não precisa iniciar a API, criar `.env`, obter credenciais externas ou instalar SQL Server. A primeira restauração requer acesso ao NuGet. Para usar PowerShell/CMD, execute os três comandos equivalentes da seção de testes do README.

O processo retorna `0` quando todos os testes passam; falhas de restore, build ou testes retornam um código diferente de zero. O relatório `TestResults/tests.trx` mostra cada caso e seu resultado. A cobertura Cobertura fica em `TestResults/<id-da-execucao>/coverage.cobertura.xml`.

Após compilar, é possível executar grupos separadamente:

```bash
# Apenas os 8 cenários Gherkin
dotnet test tests/HydroGuard.Tests -c Release --no-build --filter 'Category=bdd'

# Testes de API e do validador de contratos
dotnet test tests/HydroGuard.Tests -c Release --no-build --filter 'FullyQualifiedName~HydroGuard.Tests.Api'
```

## 1. Cenários BDD em Gherkin

Os arquivos `.feature` usam português (`# language: pt`) e a tag `@bdd`. Os passos em `Steps/HydroGuardSteps.cs` executam requisições HTTP na aplicação real hospedada pelo `WebApplicationFactory`. Não são apenas documentos: cada cenário aparece como teste no xUnit e no relatório TRX.

| Arquivo em `tests/HydroGuard.Tests/Features/` | Cenário | Resultado esperado |
| --- | --- | --- |
| Autenticacao.feature | Credenciais válidas | 200, token JWT com contrato válido e acesso a uma rota protegida |
| Autenticacao.feature | Senha incorreta | 401, mensagem de erro e ausência de token |
| Monitoramento.feature | Cadastro de medidor | 201 e medidor encontrado na listagem com os dados enviados |
| Monitoramento.feature | Serial duplicado | 400 e nenhum registro duplicado |
| Monitoramento.feature | Registro de consumo | 201, data automática e registro disponível no histórico do medidor |
| Monitoramento.feature | Consumo negativo | 400 com erro de validação e histórico sem o registro inválido |
| Monitoramento.feature | Aumento de 100 para 300 litros | Alerta de severidade Alta e inclusão no relatório gerencial |
| Monitoramento.feature | Aumento de 100 para 140 litros | Nenhum alerta: o consumo precisa ultrapassar os 40% |

Exemplo executável de caminho negativo:

```gherkin
Cenário: Recusar um consumo negativo
  Dado que existe um medidor para monitoramento
  Quando tento registrar um consumo de -10 litros
  Então o consumo é recusado por validação e o histórico permanece vazio
```

O `Contexto` da funcionalidade de monitoramento autentica o administrador antes de cada cenário. As credenciais `admin/admin123` são dados exclusivos do banco de testes, criados pela factory, e não são as credenciais de staging/produção.

## 2. Cobertura das APIs

`tests/HydroGuard.Tests/Api/ApiContractTests.cs` cobre todas as **8 operações de negócio** e os **2 endpoints de saúde**. Cada resposta JSON passa por validação de status, Content-Type, JSON Schema e asserções sobre valores ou efeitos do comportamento. O fluxo de login usa o JWT emitido pela própria API nas rotas protegidas.

| Método e rota | Caminho positivo | Caminhos negativos / limites | Contratos |
| --- | --- | --- | --- |
| POST /api/auth/login | 200; JWT autoriza consulta de medidores | 401 para senha errada e usuário inexistente; 400 para campos obrigatórios ausentes | token, error, validationProblem |
| GET /api/medidores | 200; paginação e dados persistidos | 401 sem token ou com token inválido; normalização de paginação inválida | medidores |
| POST /api/medidores | 201; dados retornados, Location e posterior listagem | 400 para serial duplicado/campos ausentes; 401 sem token ou com token inválido | medidor, error, validationProblem |
| GET /api/consumos | 200; histórico, paginação, filtros por medidor/data e resultado vazio | 401 sem token ou com token inválido; normalização de paginação inválida | consumos |
| POST /api/consumos | 201; valor, medidor, data automática, Location e persistência | 400 para litros negativos; 404 para medidor inexistente; 401 sem token ou com token inválido; falhas não persistem | consumo, error, validationProblem |
| GET /api/alertas/vazamentos | 200; alertas Baixa/Média/Alta com medidor e mensagem corretos | 401 sem token ou com token inválido; lista vazia em consumo normal e no limite de 40% | alertas |
| GET /api/esg/indicadores | 200; novo consumo incrementa o total mensal e atualiza média diária | 401 sem token ou com token inválido | esg |
| GET /api/relatorios/consumo | 200; consumos agregados, medidor e alerta crítico criado pelo serviço de alertas | 401 sem token ou com token inválido | relatorio |
| GET /healthz | 200 anônimo; estado healthy e ambiente Testing | Indisponibilidade de SQL não é simulada no InMemory | health |
| GET /livez | 200 anônimo; estado alive | Não consulta o banco | live |

As respostas 401 do middleware JWT nas rotas protegidas não possuem corpo JSON: os testes validam status, cabeçalho `WWW-Authenticate: Bearer` e corpo vazio. O login com credenciais inválidas retorna JSON e tem contrato próprio. Erros de DataAnnotations retornam `ValidationProblemDetails` (400); erros de negócio retornam `{ "error": true, "message": "..." }` (400/404).

### Contratos JSON Schema

Os contratos estão em `tests/HydroGuard.Tests/Contracts/api.schema.json`, escritos independentemente dos ViewModels, com JSON Schema Draft 2020-12. `Support/ContractAssert.cs` usa **JsonSchema.Net** para avaliar o contrato selecionado em `$defs`, incluindo referências internas para itens de listas e objetos aninhados.

As regras verificam campos obrigatórios, tipos, identificadores positivos, litros não negativos, paginação, tamanho dos textos, enum de severidade, score ESG entre 0 e 100, data/hora e formato do JWT. A validação de `format` está habilitada. `additionalProperties: false` impede exposição silenciosa de campos extras; alterações intencionais da API exigem revisar o contrato.

`SchemaValidationTests` demonstra que o validador rejeita campo ausente, tipo errado, token malformado, item incompleto em lista, data inválida, severidade fora do enum e campo extra inesperado.

### Isolamento e integração

Cada novo teste HTTP e cada cenário BDD cria sua própria `ApiSession`, factory e banco InMemory, descartados ao final. Os dados são preparados para o cenário, evitando dependência da ordem de execução ou de dados de um servidor compartilhado.

Os testes atravessam o pipeline ASP.NET Core, autenticação JWT, controllers, serviços, repositórios e EF Core. Também verificam a comunicação entre os componentes de medição, consumos, alertas, relatório e ESG. O HydroGuard é uma única aplicação; não há microserviços externos para simular.

O InMemory não valida SQL Server, índices únicos, traduções SQL ou migrations. O job `container` já existente complementa essa cobertura com SQL Server real e smoke HTTP. A suíte de contratos roda no host de testes; o smoke em containers continua verificando readiness, frontend, Swagger, 401, login, medidores e ESG. Não se trata da mesma cobertura em dois bancos.

## 3. GitHub Actions

O workflow `.github/workflows/ci-cd.yml` executa restore, build e **toda a suíte** no job `build-test`, em pull requests e pushes para `main`, além de execução manual. O Reqnroll gera os testes Gherkin durante o build, sem um segundo comando de execução.

O step **Testes de API, BDD, contratos JSON Schema e health checks** executa `dotnet test` com TRX e cobertura. O artefato `test-results` é publicado mesmo se testes falharem. Se o job falhar, os jobs dependentes de container e deploy ficam bloqueados. Depois de abrir uma execução em Actions, baixe `test-results` para consultar o TRX e a cobertura.

Os prints antigos de 20 testes no README são evidências históricas do pipeline anterior. Para comprovar esta implementação, use o relatório da execução atual, que inclui os testes novos.

## Validação local desta entrega

O script `scripts/test.sh` foi executado a partir de outro diretório com SDK .NET 8.0.427. O build Release terminou sem erros ou avisos, e a suíte executou **73 testes: 73 aprovados, 0 falhas e 0 ignorados**:

- 20 testes originais;
- 8 cenários Gherkin;
- 38 casos de API, incluindo os exemplos parametrizados;
- 7 casos que verificam a rejeição de JSONs incompatíveis com os contratos.

A execução com SQL Server e a execução remota do GitHub Actions não foram realizadas nesta entrega. O smoke e os jobs de container/deploy existentes foram preservados.

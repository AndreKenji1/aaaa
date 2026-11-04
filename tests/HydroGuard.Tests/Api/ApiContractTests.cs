using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HydroGuard.Tests.Support;

namespace HydroGuard.Tests.Api;

public class ApiContractTests : IDisposable
{
  private readonly ApiSession _api = new();

  [Fact]
  public async Task Login_TokenPermiteAcessarRotaProtegida()
  {
    await _api.LoginAsync();
    using var response = await _api.Client.GetAsync("/api/medidores");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "medidores");
    Assert.Equal("HGTEST001", Assert.Single(json["items"]!.AsArray())!["serial"]!.GetValue<string>());
  }

  [Theory]
  [InlineData("admin", "senhaerrada")]
  [InlineData("usuario-inexistente", "admin123")]
  public async Task Login_CredenciaisInvalidasRetornamErroSemToken(string user, string password)
  {
    using var response = await _api.Client.PostAsJsonAsync("/api/auth/login", new { username = user, password });
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.Unauthorized, "error");
    Assert.Equal("Credenciais inválidas", json["message"]!.GetValue<string>());
    Assert.Null(json["token"]);
  }

  [Theory]
  [InlineData("/api/auth/login", "{}", "Username")]
  [InlineData("/api/auth/login", "{\"username\":\"admin\"}", "Password")]
  [InlineData("/api/medidores", "{}", "Nome")]
  [InlineData("/api/medidores", "{\"nome\":\"Teste\",\"serial\":\"HG-VALID\"}", "Localizacao")]
  [InlineData("/api/consumos", "{\"medidorId\":1,\"litrosConsumidos\":-1}", "LitrosConsumidos")]
  public async Task Post_DadosInvalidosRetornamValidationProblem(string route, string payload, string field)
  {
    await _api.LoginAsync();
    using var response = await _api.Client.PostAsync(route,
        new StringContent(payload, System.Text.Encoding.UTF8, "application/json"));
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.BadRequest, "validationProblem");
    Assert.True(json["errors"]!.AsObject().ContainsKey(field));
  }

  public static IEnumerable<object[]> ProtectedOperations()
  {
    foreach (var (method, path) in new[]
    {
      ("GET", "/api/medidores"), ("POST", "/api/medidores"),
      ("GET", "/api/consumos"), ("POST", "/api/consumos"),
      ("GET", "/api/alertas/vazamentos"), ("GET", "/api/esg/indicadores"),
      ("GET", "/api/relatorios/consumo")
    })
      foreach (var invalidToken in new[] { false, true })
        yield return new object[] { method, path, invalidToken };
  }

  [Theory]
  [MemberData(nameof(ProtectedOperations))]
  public async Task OperacaoProtegida_SemTokenOuTokenInvalidoRetorna401(
      string method, string path, bool invalidToken)
  {
    using var request = new HttpRequestMessage(new HttpMethod(method), path);
    if (invalidToken)
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "token-invalido");
    if (method == "POST") request.Content = JsonContent.Create(new { });
    using var response = await _api.Client.SendAsync(request);
    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    Assert.Contains(response.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    // O middleware JWT responde sem corpo; não há contrato JSON a validar aqui.
    Assert.Empty(await response.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task Medidores_CriarListarEPaginarPreservaDados()
  {
    await _api.LoginAsync();
    var id = await _api.CreateMeterAsync();
    using var response = await _api.Client.GetAsync("/api/medidores?page=2&pageSize=1");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "medidores");
    Assert.Equal(2, json["page"]!.GetValue<int>());
    Assert.Equal(1, json["pageSize"]!.GetValue<int>());
    Assert.Equal(2, json["totalRecords"]!.GetValue<int>());
    var meter = Assert.Single(json["items"]!.AsArray())!;
    Assert.Equal(id, meter["id"]!.GetValue<int>());
    Assert.Equal("HG-CONTRACT-001", meter["serial"]!.GetValue<string>());
  }

  [Fact]
  public async Task Medidores_SerialDuplicadoRetorna400SemDuplicarRegistro()
  {
    await _api.LoginAsync();
    using var response = await _api.Client.PostAsJsonAsync("/api/medidores", new
    {
      nome = "Duplicado", serial = "HGTEST001", localizacao = "Bloco C"
    });
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.BadRequest, "error");
    Assert.Equal("O serial do medidor já está cadastrado", json["message"]!.GetValue<string>());
    using var list = await _api.Client.GetAsync("/api/medidores");
    var meters = await ApiSession.AssertJsonAsync(list, HttpStatusCode.OK, "medidores");
    Assert.Equal(1, meters["totalRecords"]!.GetValue<int>());
  }

  [Theory]
  [InlineData("/api/medidores", "medidores")]
  [InlineData("/api/consumos", "consumos")]
  public async Task Listagem_NormalizaPaginacaoInvalida(string path, string contract)
  {
    await _api.LoginAsync();
    using var response = await _api.Client.GetAsync(path + "?page=0&pageSize=-1");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, contract);
    Assert.Equal(1, json["page"]!.GetValue<int>());
    Assert.Equal(10, json["pageSize"]!.GetValue<int>());
  }

  [Fact]
  public async Task Consumos_RegistroApareceNoHistoricoFiltradoPorMedidorEData()
  {
    await _api.LoginAsync();
    var meterId = await _api.CreateMeterAsync();
    var created = await _api.RecordConsumptionAsync(meterId, 350.5);
    var day = DateTime.UtcNow.Date;
    var query = $"/api/consumos?medidorId={meterId}&dataInicial={day:yyyy-MM-dd}&dataFinal={day.AddDays(1):yyyy-MM-dd}&page=1&pageSize=1";
    using var response = await _api.Client.GetAsync(query);
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "consumos");
    Assert.Equal(1, json["totalRecords"]!.GetValue<int>());
    Assert.Equal(1, json["page"]!.GetValue<int>());
    Assert.Equal(1, json["pageSize"]!.GetValue<int>());
    var item = Assert.Single(json["items"]!.AsArray())!;
    Assert.Equal(created["id"]!.GetValue<int>(), item["id"]!.GetValue<int>());
    Assert.Equal(meterId, item["medidorId"]!.GetValue<int>());
    Assert.Equal(350.5, item["litrosConsumidos"]!.GetValue<double>());
    using var emptyResponse = await _api.Client.GetAsync(
        $"/api/consumos?medidorId={meterId}&dataFinal={day.AddDays(-1):yyyy-MM-dd}");
    var empty = await ApiSession.AssertJsonAsync(emptyResponse, HttpStatusCode.OK, "consumos");
    Assert.Equal(0, empty["totalRecords"]!.GetValue<int>());
    Assert.Empty(empty["items"]!.AsArray());
  }

  [Fact]
  public async Task Consumos_MedidorInexistenteRetorna404SemPersistir()
  {
    await _api.LoginAsync();
    using var response = await _api.Client.PostAsJsonAsync("/api/consumos", new
    {
      medidorId = 999999, litrosConsumidos = 100
    });
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.NotFound, "error");
    Assert.Equal("Medidor não encontrado", json["message"]!.GetValue<string>());
    using var list = await _api.Client.GetAsync("/api/consumos?medidorId=999999");
    var history = await ApiSession.AssertJsonAsync(list, HttpStatusCode.OK, "consumos");
    Assert.Equal(0, history["totalRecords"]!.GetValue<int>());
  }

  [Fact]
  public async Task Consumos_NegativoRetorna400SemAlterarHistorico()
  {
    await _api.LoginAsync();
    var meterId = await _api.CreateMeterAsync();
    using var response = await _api.Client.PostAsJsonAsync("/api/consumos", new
    {
      medidorId = meterId, litrosConsumidos = -10
    });
    await ApiSession.AssertJsonAsync(response, HttpStatusCode.BadRequest, "validationProblem");
    using var list = await _api.Client.GetAsync($"/api/consumos?medidorId={meterId}");
    var history = await ApiSession.AssertJsonAsync(list, HttpStatusCode.OK, "consumos");
    Assert.Equal(0, history["totalRecords"]!.GetValue<int>());
  }

  [Theory]
  [InlineData(100, null)]
  [InlineData(140, null)]
  [InlineData(145, "Baixa")]
  [InlineData(170, "Média")]
  [InlineData(200, "Alta")]
  public async Task Alertas_LimiteDe40PorCentoESeveridades(double latest, string? severity)
  {
    await _api.LoginAsync();
    var id = await _api.CreateMeterAsync();
    await _api.RecordConsumptionAsync(id, 100);
    await _api.RecordConsumptionAsync(id, latest);
    using var response = await _api.Client.GetAsync("/api/alertas/vazamentos");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "alertas");
    if (severity is null)
      Assert.Empty(json.AsArray());
    else
    {
      var alert = Assert.Single(json.AsArray())!;
      Assert.Equal(id, alert["medidorId"]!.GetValue<int>());
      Assert.True(alert["vazamentoDetectado"]!.GetValue<bool>());
      Assert.Equal(severity, alert["severidade"]!.GetValue<string>());
      Assert.Equal("Possível vazamento detectado", alert["mensagem"]!.GetValue<string>());
    }
  }

  [Fact]
  public async Task Esg_ConsumoRegistradoAtualizaTotalMensal()
  {
    await _api.LoginAsync();
    using var beforeResponse = await _api.Client.GetAsync("/api/esg/indicadores");
    var before = await ApiSession.AssertJsonAsync(beforeResponse, HttpStatusCode.OK, "esg");
    var id = await _api.CreateMeterAsync();
    await _api.RecordConsumptionAsync(id, 350.5);
    using var response = await _api.Client.GetAsync("/api/esg/indicadores");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "esg");
    Assert.Equal(before["consumoMensal"]!.GetValue<double>() + 350.5,
        json["consumoMensal"]!.GetValue<double>());
    Assert.Equal(Math.Round(json["consumoMensal"]!.GetValue<double>() /
        DateTime.DaysInMonth(DateTime.UtcNow.Year, DateTime.UtcNow.Month), 2),
        json["consumoMedioDiario"]!.GetValue<double>());
  }

  [Fact]
  public async Task Relatorios_ConsumosEAlertaCriticoIntegramRelatorio()
  {
    await _api.LoginAsync();
    var id = await _api.CreateMeterAsync();
    await _api.RecordConsumptionAsync(id, 100);
    await _api.RecordConsumptionAsync(id, 300);
    using var alerts = await _api.Client.GetAsync("/api/alertas/vazamentos");
    await ApiSession.AssertJsonAsync(alerts, HttpStatusCode.OK, "alertas");
    using var response = await _api.Client.GetAsync("/api/relatorios/consumo");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "relatorio");
    var meter = Assert.Single(json["topMedidores"]!.AsArray(),
        m => m!["medidorId"]!.GetValue<int>() == id)!;
    Assert.Equal(400, meter["totalConsumo"]!.GetValue<double>());
    Assert.Equal("Medidor Integração", meter["nome"]!.GetValue<string>());
    var alert = Assert.Single(json["alertasCriticos"]!.AsArray())!;
    Assert.Equal(id, alert["medidorId"]!.GetValue<int>());
    Assert.Equal("Alta", alert["severidade"]!.GetValue<string>());
    Assert.True(json["mediaPorPeriodo"]!.GetValue<double>() > 0);
    Assert.True(json["projecaoFutura"]!.GetValue<double>() > 0);
  }

  [Theory]
  [InlineData("/healthz", "health", "healthy")]
  [InlineData("/livez", "live", "alive")]
  public async Task Health_EndpointsPublicosRetornamContrato(string path, string contract, string status)
  {
    using var response = await _api.Client.GetAsync(path);
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, contract);
    Assert.Equal(status, json["status"]!.GetValue<string>());
    if (path == "/healthz") Assert.Equal("Testing", json["environment"]!.GetValue<string>());
  }

  public void Dispose() => _api.Dispose();
}

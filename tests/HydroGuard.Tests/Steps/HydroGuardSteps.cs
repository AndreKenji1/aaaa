using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using HydroGuard.Tests.Support;
using Reqnroll;

namespace HydroGuard.Tests.Steps;

[Binding]
public sealed class HydroGuardSteps
{
  private readonly ApiSession _api = new();
  private HttpResponseMessage? _response;
  private int _meterId;
  private string _serial = "";
  private JsonNode? _consumption;

  [AfterScenario]
  public void Cleanup()
  {
    _response?.Dispose();
    _api.Dispose();
  }

  [When(@"faço login com usuário ""(.*)"" e senha ""(.*)""")]
  public async Task Login(string username, string password) =>
      _response = await _api.Client.PostAsJsonAsync("/api/auth/login", new { username, password });

  [Then("recebo um token JWT válido que permite consultar os medidores")]
  public async Task LoginAuthorized()
  {
    var json = await ApiSession.AssertJsonAsync(_response!, HttpStatusCode.OK, "token");
    _api.Client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", json["token"]!.GetValue<string>());
    using var response = await _api.Client.GetAsync("/api/medidores");
    await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "medidores");
  }

  [Then(@"o acesso é negado com a mensagem ""(.*)"" e sem token")]
  public async Task LoginDenied(string message)
  {
    var json = await ApiSession.AssertJsonAsync(_response!, HttpStatusCode.Unauthorized, "error");
    Assert.Equal(message, json["message"]!.GetValue<string>());
    Assert.Null(json["token"]);
  }

  [Given("que estou autenticado como administrador")]
  public Task Authenticate() => _api.LoginAsync();

  [When(@"cadastro um medidor com serial ""(.*)""")]
  public async Task CreateMeter(string serial)
  {
    _serial = serial;
    _meterId = await _api.CreateMeterAsync(serial);
  }

  [Then("o medidor é criado e aparece na listagem com o mesmo serial")]
  public async Task MeterListed()
  {
    using var response = await _api.Client.GetAsync("/api/medidores");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "medidores");
    var item = Assert.Single(json["items"]!.AsArray(), m => m!["id"]!.GetValue<int>() == _meterId)!;
    Assert.Equal(_serial, item["serial"]!.GetValue<string>());
  }

  [Given(@"que já existe o medidor com serial ""(.*)""")]
  public async Task ExistingMeter(string serial)
  {
    using var response = await _api.Client.GetAsync("/api/medidores");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "medidores");
    Assert.Single(json["items"]!.AsArray(), m => m!["serial"]!.GetValue<string>() == serial);
  }

  [When(@"tento cadastrar outro medidor com serial ""(.*)""")]
  public async Task DuplicateMeter(string serial) =>
      _response = await _api.Client.PostAsJsonAsync("/api/medidores", new
      {
        nome = "Medidor duplicado", serial, localizacao = "Bloco C"
      });

  [Then("o cadastro é recusado sem duplicar o medidor")]
  public async Task DuplicateDenied()
  {
    var json = await ApiSession.AssertJsonAsync(_response!, HttpStatusCode.BadRequest, "error");
    Assert.Equal("O serial do medidor já está cadastrado", json["message"]!.GetValue<string>());
    using var response = await _api.Client.GetAsync("/api/medidores");
    var list = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "medidores");
    Assert.Equal(1, list["totalRecords"]!.GetValue<int>());
  }

  [Given("que existe um medidor para monitoramento")]
  public async Task MonitoringMeter() => _meterId = await _api.CreateMeterAsync("HG-BDD-MONITOR");

  [Given(@"que esse medidor possui um consumo anterior de (\d+) litros")]
  public async Task HistoricalConsumption(int liters) =>
      await _api.RecordConsumptionAsync(_meterId, liters);

  [When(@"registro (\d+) litros de consumo")]
  public async Task RecordConsumption(int liters) =>
      _consumption = await _api.RecordConsumptionAsync(_meterId, liters);

  [Then("o consumo é criado com data automática e aparece no histórico desse medidor")]
  public async Task ConsumptionListed()
  {
    var json = await History();
    var item = Assert.Single(json["items"]!.AsArray())!;
    Assert.Equal(_consumption!["id"]!.GetValue<int>(), item["id"]!.GetValue<int>());
    Assert.Equal(_meterId, item["medidorId"]!.GetValue<int>());
    Assert.Equal(_consumption["litrosConsumidos"]!.GetValue<double>(), item["litrosConsumidos"]!.GetValue<double>());
  }

  [When(@"tento registrar um consumo de (-?\d+) litros")]
  public async Task InvalidConsumption(int liters) =>
      _response = await _api.Client.PostAsJsonAsync("/api/consumos", new
      {
        medidorId = _meterId, litrosConsumidos = liters
      });

  [Then("o consumo é recusado por validação e o histórico permanece vazio")]
  public async Task ConsumptionDenied()
  {
    var error = await ApiSession.AssertJsonAsync(_response!, HttpStatusCode.BadRequest, "validationProblem");
    Assert.NotNull(error["errors"]!["LitrosConsumidos"]);
    var history = await History();
    Assert.Equal(0, history["totalRecords"]!.GetValue<int>());
    Assert.Empty(history["items"]!.AsArray());
  }

  [When("consulto os possíveis vazamentos")]
  public async Task RequestAlerts() => _response = await _api.Client.GetAsync("/api/alertas/vazamentos");

  [Then(@"recebo um alerta de severidade ""(.*)"" para esse medidor")]
  public async Task AlertDetected(string severity)
  {
    var json = await ApiSession.AssertJsonAsync(_response!, HttpStatusCode.OK, "alertas");
    var alert = Assert.Single(json.AsArray())!;
    Assert.Equal(_meterId, alert["medidorId"]!.GetValue<int>());
    Assert.True(alert["vazamentoDetectado"]!.GetValue<bool>());
    Assert.Equal(severity, alert["severidade"]!.GetValue<string>());
  }

  [Then("esse alerta crítico aparece no relatório gerencial")]
  public async Task ReportContainsAlert()
  {
    using var response = await _api.Client.GetAsync("/api/relatorios/consumo");
    var json = await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "relatorio");
    var alert = Assert.Single(json["alertasCriticos"]!.AsArray())!;
    Assert.Equal(_meterId, alert["medidorId"]!.GetValue<int>());
    Assert.Equal("Alta", alert["severidade"]!.GetValue<string>());
  }

  [Then("não recebo alertas de vazamento")]
  public async Task NoAlerts()
  {
    var json = await ApiSession.AssertJsonAsync(_response!, HttpStatusCode.OK, "alertas");
    Assert.Empty(json.AsArray());
  }

  private async Task<JsonNode> History()
  {
    using var response = await _api.Client.GetAsync($"/api/consumos?medidorId={_meterId}");
    return await ApiSession.AssertJsonAsync(response, HttpStatusCode.OK, "consumos");
  }
}

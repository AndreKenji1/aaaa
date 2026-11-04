using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace HydroGuard.Tests.Support;

// Uma aplicação e um banco novos por teste/cenário: sem dependência de ordem.
public sealed class ApiSession : IDisposable
{
  private readonly HydroGuardWebApplicationFactory _factory = new();
  public HttpClient Client { get; }

  public ApiSession() => Client = _factory.CreateClient();

  public async Task LoginAsync()
  {
    using var response = await Client.PostAsJsonAsync("/api/auth/login", new
    {
      username = "admin", password = "admin123"
    });
    var json = await AssertJsonAsync(response, HttpStatusCode.OK, "token");
    Client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Bearer", json["token"]!.GetValue<string>());
  }

  public static async Task<JsonNode> AssertJsonAsync(
      HttpResponseMessage response, HttpStatusCode status, string contract)
  {
    Assert.Equal(status, response.StatusCode);
    var mediaType = response.Content.Headers.ContentType?.MediaType;
    Assert.True(mediaType is "application/json" or "application/problem+json",
        $"Content-Type JSON esperado, recebido: {mediaType}");
    var body = await response.Content.ReadAsStringAsync();
    var json = JsonNode.Parse(body);
    Assert.NotNull(json);
    ContractAssert.Matches(contract, json);
    return json;
  }

  public async Task<int> CreateMeterAsync(string serial = "HG-CONTRACT-001")
  {
    using var response = await Client.PostAsJsonAsync("/api/medidores", new
    {
      nome = "Medidor Integração", serial, localizacao = "Bloco C"
    });
    var json = await AssertJsonAsync(response, HttpStatusCode.Created, "medidor");
    Assert.NotNull(response.Headers.Location);
    Assert.Equal("Medidor Integração", json["nome"]!.GetValue<string>());
    Assert.Equal(serial, json["serial"]!.GetValue<string>());
    Assert.Equal("Bloco C", json["localizacao"]!.GetValue<string>());
    return json["id"]!.GetValue<int>();
  }

  public async Task<JsonNode> RecordConsumptionAsync(int meterId, double liters)
  {
    var before = DateTime.UtcNow;
    using var response = await Client.PostAsJsonAsync("/api/consumos", new
    {
      medidorId = meterId, litrosConsumidos = liters
    });
    var json = await AssertJsonAsync(response, HttpStatusCode.Created, "consumo");
    Assert.NotNull(response.Headers.Location);
    Assert.Equal(meterId, json["medidorId"]!.GetValue<int>());
    Assert.Equal(liters, json["litrosConsumidos"]!.GetValue<double>());
    Assert.InRange(json["dataRegistro"]!.GetValue<DateTime>(), before, DateTime.UtcNow);
    return json;
  }

  public void Dispose()
  {
    Client.Dispose();
    _factory.Dispose();
  }
}

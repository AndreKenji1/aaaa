using System.Text.Json.Nodes;
using HydroGuard.Tests.Support;

namespace HydroGuard.Tests.Api;

// Comprova que o validador rejeita regressões de contrato, inclusive em objetos aninhados.
public class SchemaValidationTests
{
  [Theory]
  [InlineData("token", "{}")]
  [InlineData("token", "{\"token\":123}")]
  [InlineData("token", "{\"token\":\"nao-e-jwt\"}")]
  [InlineData("medidores", "{\"page\":1,\"pageSize\":10,\"totalRecords\":1,\"items\":[{\"id\":1}]}")]
  [InlineData("consumo", "{\"id\":1,\"medidorId\":1,\"litrosConsumidos\":10,\"dataRegistro\":\"ontem\"}")]
  [InlineData("alertas", "[{\"medidorId\":1,\"vazamentoDetectado\":true,\"severidade\":\"Urgente\",\"mensagem\":\"Vazamento\"}]")]
  [InlineData("live", "{\"status\":\"alive\",\"passwordHash\":\"nao-deve-ser-exposto\"}")]
  public void ContratoInvalidoDeveSerRejeitado(string contract, string body) =>
      Assert.False(ContractAssert.Evaluate(contract, JsonNode.Parse(body)).IsValid);
}

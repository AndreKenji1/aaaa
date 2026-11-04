using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace HydroGuard.Tests.Support;

public static class ContractAssert
{
  private static readonly ConcurrentDictionary<string, JsonSchema> Schemas = new();

  public static EvaluationResults Evaluate(string contract, JsonNode? json)
  {
    var schema = Schemas.GetOrAdd(contract, name =>
    {
      var path = Path.Combine(AppContext.BaseDirectory, "Contracts", "api.schema.json");
      var root = JsonNode.Parse(File.ReadAllText(path))!;
      if (root["$defs"]?[name] is null)
        throw new ArgumentException($"Contrato desconhecido: {name}");
      root["$ref"] = $"#/$defs/{name}";
      return JsonSchema.FromText(root.ToJsonString());
    });
    return schema.Evaluate(json, new EvaluationOptions
    {
      OutputFormat = OutputFormat.List,
      RequireFormatValidation = true
    });
  }

  public static void Matches(string contract, JsonNode? json)
  {
    var result = Evaluate(contract, json);
    // Não inclui o corpo na mensagem de falha para não expor o JWT.
    Assert.True(result.IsValid,
        $"Resposta viola o contrato '{contract}': {JsonSerializer.Serialize(result)}");
  }
}

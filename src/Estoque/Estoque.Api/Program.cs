using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDefaultAWSOptions(builder.Configuration.GetAWSOptions("Aws"));
builder.Services.AddAWSService<IAmazonDynamoDB>();

// Na Lambda, traduz a chamada da Function URL para uma requisição HTTP normal. Local, não faz nada.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// O front roda em outro endereço, então o navegador exige que a API autorize a origem (CORS).
// Só leitura: a vitrine não altera nada no Estoque.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().WithMethods("GET")));

var app = builder.Build();

app.UseCors();

var tabela = builder.Configuration["Aws:TabelaEstoque"] ?? "Estoque";

// Scan lê a tabela inteira: aceitável para um catálogo pequeno. Com milhares de produtos,
// o certo seria paginar ou manter uma visão própria para a vitrine.
app.MapGet("/produtos", async (IAmazonDynamoDB dynamo, CancellationToken ct) =>
{
    var resposta = await dynamo.ScanAsync(new ScanRequest { TableName = tabela, ConsistentRead = true }, ct);

    var produtos = (resposta.Items ?? [])
        .Select(item => new ProdutoResponse(
            item["ProdutoId"].S,
            item.TryGetValue("Nome", out var nome) ? nome.S : item["ProdutoId"].S,
            item.TryGetValue("Preco", out var preco) ? decimal.Parse(preco.N, CultureInfo.InvariantCulture) : 0m,
            int.Parse(item["Quantidade"].N, CultureInfo.InvariantCulture)))
        .OrderBy(p => p.Preco);

    return Results.Ok(produtos);
});

app.Run();

// Contrato público da vitrine: não expõe o formato interno da tabela.
internal sealed record ProdutoResponse(string Id, string Nome, decimal Preco, int Disponivel);

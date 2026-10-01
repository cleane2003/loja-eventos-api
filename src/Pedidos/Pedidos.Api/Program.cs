using Contracts;
using Pedidos.Api;
using Pedidos.Domain;
using Pedidos.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

// Na Lambda, traduz a chamada da Function URL para uma requisição HTTP normal do ASP.NET Core.
// Rodando local (dotnet run), não faz nada e o Kestrel continua atendendo.
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

// O front roda em outro endereço, então o navegador exige que a API autorize a origem (CORS).
// Location é exposto para o front saber onde consultar o status depois do 202.
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .AllowAnyOrigin()
    .WithMethods("GET", "POST")
    .WithHeaders("Content-Type")
    .WithExposedHeaders("Location")));

var app = builder.Build();

app.UseCors();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapPost("/pedidos", async (CriarPedidoRequest request, IPedidoRepository repositorio, IEventPublisher publisher, CancellationToken ct) =>
{
    Pedido pedido;
    try
    {
        var itens = (request.Itens ?? []).Select(i => new ItemPedido(i.ProdutoId, i.Quantidade, i.PrecoUnitario));
        pedido = Pedido.Criar(request.ClienteId, itens);
    }
    catch (DomainException ex)
    {
        return Results.BadRequest(new { erro = ex.Message });
    }

    await repositorio.SalvarAsync(pedido, ct);

    // RISCO DE ESCRITA DUPLA: se a aplicação cair entre o SalvarAsync e o PublicarAsync,
    // o pedido fica salvo mas o evento nunca sai. Resolvido com Outbox (DynamoDB Streams) na fase 7.
    var evento = EventEnvelope.Criar(
        PedidoCriado.Tipo,
        correlationId: pedido.Id,
        new PedidoCriado(
            pedido.Id,
            pedido.ClienteId,
            pedido.Itens.Select(i => new ItemPedidoCriado(i.ProdutoId, i.Quantidade, i.PrecoUnitario)).ToList(),
            pedido.ValorTotal));

    await publisher.PublicarAsync(evento, ct);

    // 202: "recebi e estou processando". A confirmação acontece depois, de forma assíncrona.
    return Results.Accepted($"/pedidos/{pedido.Id}", new { pedido.Id, Status = pedido.Status.ToString() });
});

app.MapGet("/pedidos/{id}", async (string id, IPedidoRepository repositorio, CancellationToken ct) =>
    await repositorio.ObterPorIdAsync(id, ct) is { } pedido
        ? Results.Ok(PedidoResponse.De(pedido))
        : Results.NotFound());

app.Run();

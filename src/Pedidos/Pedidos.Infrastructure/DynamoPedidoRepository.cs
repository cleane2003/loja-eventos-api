using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Pedidos.Domain;

namespace Pedidos.Infrastructure;

// Adaptador (hexagonal): implementa a porta IPedidoRepository usando DynamoDB.
// O pedido inteiro, com os itens, vira UM item na tabela: lido e gravado de uma vez só.
public sealed class DynamoPedidoRepository(IAmazonDynamoDB dynamo, InfraOptions opcoes) : IPedidoRepository
{
    // DynamoDB recebe números como texto. Sem isso, numa máquina pt-BR 50.5 viraria "50,5".
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    public async Task SalvarAsync(Pedido pedido, CancellationToken ct = default)
    {
        var item = new Dictionary<string, AttributeValue>
        {
            ["PedidoId"] = new() { S = pedido.Id },
            ["ClienteId"] = new() { S = pedido.ClienteId },
            ["Status"] = new() { S = pedido.Status.ToString() },
            ["CriadoEm"] = new() { S = pedido.CriadoEm.ToString("O") },
            // Guardado só para facilitar a leitura no console; o domínio sempre recalcula.
            ["ValorTotal"] = new() { N = pedido.ValorTotal.ToString(Invariante) },
            ["Itens"] = new()
            {
                L = pedido.Itens.Select(i => new AttributeValue
                {
                    M = new Dictionary<string, AttributeValue>
                    {
                        ["ProdutoId"] = new() { S = i.ProdutoId },
                        ["Quantidade"] = new() { N = i.Quantidade.ToString(Invariante) },
                        ["PrecoUnitario"] = new() { N = i.PrecoUnitario.ToString(Invariante) }
                    }
                }).ToList()
            }
        };

        // PutItem cria ou substitui o item inteiro: serve para criar e para atualizar o status.
        await dynamo.PutItemAsync(new PutItemRequest { TableName = opcoes.TabelaPedidos, Item = item }, ct);
    }

    public async Task<Pedido?> ObterPorIdAsync(string id, CancellationToken ct = default)
    {
        var resposta = await dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = opcoes.TabelaPedidos,
            Key = new Dictionary<string, AttributeValue> { ["PedidoId"] = new() { S = id } },
            // Por padrão a leitura é eventualmente consistente e pode não ver uma gravação recém-feita.
            ConsistentRead = true
        }, ct);

        var item = resposta.Item;
        if (item is null || item.Count == 0)
            return null;

        var itens = item["Itens"].L.Select(i => new ItemPedido(
            i.M["ProdutoId"].S,
            int.Parse(i.M["Quantidade"].N, Invariante),
            decimal.Parse(i.M["PrecoUnitario"].N, Invariante)));

        return Pedido.Restaurar(
            item["PedidoId"].S,
            item["ClienteId"].S,
            itens,
            Enum.Parse<StatusPedido>(item["Status"].S),
            DateTime.Parse(item["CriadoEm"].S, Invariante, DateTimeStyles.RoundtripKind));
    }
}

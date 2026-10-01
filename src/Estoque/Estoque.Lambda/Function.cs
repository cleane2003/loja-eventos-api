using System.Globalization;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SecurityToken;
using Amazon.SimpleNotificationService;
using Contracts;
using Mensageria;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Estoque.Lambda;

// PedidoCriado      -> reserva os produtos e publica EstoqueReservado ou EstoqueIndisponivel.
// PagamentoRecusado -> compensação da saga: devolve o que foi reservado.
public class Function
{
    private const string ReservaAtiva = "Ativa";
    private const string ReservaDevolvida = "Devolvida";
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    // Criados uma vez por instância e reaproveitados entre execuções: só pagam o custo no cold start.
    private readonly IAmazonDynamoDB _dynamo;
    private readonly PublicadorSns _publicador;
    private readonly string _tabelaEstoque = Ambiente.Obter("TABELA_ESTOQUE");
    private readonly string _tabelaReservas = Ambiente.Obter("TABELA_RESERVAS");
    private readonly string _topicoReservado = Ambiente.Obter("TOPICO_ESTOQUE_RESERVADO");
    private readonly string _topicoIndisponivel = Ambiente.Obter("TOPICO_ESTOQUE_INDISPONIVEL");

    public Function() : this(new AmazonDynamoDBClient(),
        new PublicadorSns(new AmazonSimpleNotificationServiceClient(), new AmazonSecurityTokenServiceClient())) { }

    public Function(IAmazonDynamoDB dynamo, PublicadorSns publicador)
    {
        _dynamo = dynamo;
        _publicador = publicador;
    }

    public Task<SQSBatchResponse> FunctionHandler(SQSEvent lote, ILambdaContext context) =>
        ProcessadorLote.ProcessarAsync(lote, context.Logger, evento => evento.EventType switch
        {
            PedidoCriado.Tipo => ReservarAsync(evento.CorrelationId, evento.Dados<PedidoCriado>(), context.Logger),
            PagamentoRecusado.Tipo => DevolverAsync(evento.CorrelationId, evento.Dados<PagamentoRecusado>(), context.Logger),
            _ => ProcessadorLote.Ignorar(evento, context.Logger)
        });

    private async Task ReservarAsync(string correlationId, PedidoCriado pedido, ILambdaLogger log)
    {
        if (pedido.Itens is null)
            throw new FormatException("PedidoCriado sem itens.");

        log.LogInformation($"[{correlationId}] Reservando estoque do pedido {pedido.PedidoId}");

        // DynamoDB não aceita alterar o mesmo item duas vezes numa transação: soma itens repetidos.
        var produtos = pedido.Itens
            .GroupBy(i => i.ProdutoId)
            .Select(g => (ProdutoId: g.Key, Quantidade: g.Sum(i => i.Quantidade)))
            .ToList();

        var transacao = new List<TransactWriteItem> { RegistrarReserva(pedido.PedidoId, produtos) };
        transacao.AddRange(produtos.Select(p => AlterarEstoque(p.ProdutoId, -p.Quantidade)));

        try
        {
            // Tudo ou nada: se qualquer condição falhar, nenhuma alteração é aplicada.
            await _dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = transacao });
        }
        catch (TransactionCanceledException ex)
        {
            var motivos = ex.CancellationReasons ?? [];

            // Posição 0 = registro da reserva. Se falhou, este pedido já passou por aqui (mensagem repetida).
            if (motivos.Count > 0 && motivos[0].Code == "ConditionalCheckFailed")
            {
                // A publicação anterior pode ter falhado: republica, a não ser que a reserva já tenha sido desfeita.
                if (await StatusReservaAsync(pedido.PedidoId) == ReservaAtiva)
                {
                    log.LogInformation($"[{correlationId}] Pedido já reservado anteriormente, republicando.");
                    await PublicarReservadoAsync(correlationId, pedido);
                }
                return;
            }

            // Posições 1..N = produtos, na mesma ordem da lista.
            var indisponiveis = motivos
                .Skip(1)
                .Select((motivo, i) => (motivo.Code, produtos[i].ProdutoId))
                .Where(x => x.Code == "ConditionalCheckFailed")
                .Select(x => x.ProdutoId)
                .ToList();

            if (indisponiveis.Count == 0)
                throw; // Outro motivo (ex.: conflito com outra transação): deixa a SQS tentar de novo.

            log.LogInformation($"[{correlationId}] Sem estoque para: {string.Join(", ", indisponiveis)}");
            await _publicador.PublicarAsync(_topicoIndisponivel, EstoqueIndisponivel.Tipo, correlationId,
                new EstoqueIndisponivel(pedido.PedidoId, indisponiveis));
            return;
        }

        log.LogInformation($"[{correlationId}] Estoque reservado.");
        await PublicarReservadoAsync(correlationId, pedido);
    }

    // Compensação: não existe rollback entre serviços, então o Estoque desfaz a própria parte.
    private async Task DevolverAsync(string correlationId, PagamentoRecusado recusa, ILambdaLogger log)
    {
        var reserva = await ObterReservaAsync(recusa.PedidoId);
        if (reserva is null || reserva["Status"].S != ReservaAtiva)
        {
            log.LogInformation($"[{correlationId}] Nada a devolver para o pedido {recusa.PedidoId}.");
            return;
        }

        var produtos = reserva["Itens"].L
            .Select(i => (ProdutoId: i.M["ProdutoId"].S, Quantidade: int.Parse(i.M["Quantidade"].N, Invariante)))
            .ToList();

        var transacao = new List<TransactWriteItem> { MarcarDevolvida(recusa.PedidoId) };
        transacao.AddRange(produtos.Select(p => AlterarEstoque(p.ProdutoId, +p.Quantidade)));

        try
        {
            await _dynamo.TransactWriteItemsAsync(new TransactWriteItemsRequest { TransactItems = transacao });
        }
        catch (TransactionCanceledException ex) when (ex.CancellationReasons?.FirstOrDefault()?.Code == "ConditionalCheckFailed")
        {
            // Outra execução devolveu primeiro (mensagem repetida em paralelo): nada a fazer.
            log.LogInformation($"[{correlationId}] Estoque já devolvido por outra execução.");
            return;
        }

        log.LogInformation($"[{correlationId}] Estoque devolvido ({recusa.Motivo}).");
    }

    // Só grava se ainda não existir reserva para este pedido: é o que garante a idempotência.
    private TransactWriteItem RegistrarReserva(string pedidoId, List<(string ProdutoId, int Quantidade)> produtos) => new()
    {
        Put = new Put
        {
            TableName = _tabelaReservas,
            Item = new Dictionary<string, AttributeValue>
            {
                ["PedidoId"] = new() { S = pedidoId },
                ["Status"] = new() { S = ReservaAtiva },
                ["CriadoEm"] = new() { S = DateTime.UtcNow.ToString("O") },
                // Guardado para a compensação: se o pagamento falhar, sabemos o que devolver.
                ["Itens"] = new()
                {
                    L = produtos.Select(p => new AttributeValue
                    {
                        M = new Dictionary<string, AttributeValue>
                        {
                            ["ProdutoId"] = new() { S = p.ProdutoId },
                            ["Quantidade"] = new() { N = p.Quantidade.ToString(Invariante) }
                        }
                    }).ToList()
                }
            },
            ConditionExpression = "attribute_not_exists(PedidoId)"
        }
    };

    // A condição garante que duas devoluções simultâneas do mesmo pedido não somem o estoque duas vezes.
    private TransactWriteItem MarcarDevolvida(string pedidoId) => new()
    {
        Update = new Update
        {
            TableName = _tabelaReservas,
            Key = new Dictionary<string, AttributeValue> { ["PedidoId"] = new() { S = pedidoId } },
            UpdateExpression = "SET #status = :devolvida",
            ConditionExpression = "#status = :ativa",
            // "Status" é palavra reservada no DynamoDB, por isso o apelido #status.
            ExpressionAttributeNames = new Dictionary<string, string> { ["#status"] = "Status" },
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":ativa"] = new() { S = ReservaAtiva },
                [":devolvida"] = new() { S = ReservaDevolvida }
            }
        }
    };

    // Baixa (delta negativo) com condição dentro da escrita: duas execuções simultâneas
    // nunca vendem a mesma unidade, sem condição de corrida. Devolução (delta positivo) não tem condição.
    private TransactWriteItem AlterarEstoque(string produtoId, int delta)
    {
        var update = new Update
        {
            TableName = _tabelaEstoque,
            Key = new Dictionary<string, AttributeValue> { ["ProdutoId"] = new() { S = produtoId } },
            UpdateExpression = "SET Quantidade = Quantidade + :delta",
            ExpressionAttributeValues = new Dictionary<string, AttributeValue>
            {
                [":delta"] = new() { N = delta.ToString(Invariante) }
            }
        };

        if (delta < 0)
        {
            update.ConditionExpression = "Quantidade >= :necessaria";
            update.ExpressionAttributeValues[":necessaria"] = new() { N = (-delta).ToString(Invariante) };
        }

        return new TransactWriteItem { Update = update };
    }

    private async Task<Dictionary<string, AttributeValue>?> ObterReservaAsync(string pedidoId)
    {
        var resposta = await _dynamo.GetItemAsync(new GetItemRequest
        {
            TableName = _tabelaReservas,
            Key = new Dictionary<string, AttributeValue> { ["PedidoId"] = new() { S = pedidoId } },
            ConsistentRead = true
        });

        return resposta.Item is { Count: > 0 } item ? item : null;
    }

    private async Task<string?> StatusReservaAsync(string pedidoId) =>
        (await ObterReservaAsync(pedidoId))?["Status"].S;

    private Task PublicarReservadoAsync(string correlationId, PedidoCriado pedido) =>
        _publicador.PublicarAsync(_topicoReservado, EstoqueReservado.Tipo, correlationId,
            new EstoqueReservado(pedido.PedidoId, pedido.ClienteId, pedido.ValorTotal));
}

using Amazon.DynamoDBv2;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Contracts;
using Mensageria;
using Pedidos.Domain;
using Pedidos.Infrastructure;

[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Pedidos.Lambda;

// Segundo ponto de entrada do serviço de Pedidos (o primeiro é a Api).
// Reaproveita o mesmo Domain e o mesmo repositório: só muda quem chama, HTTP ou evento.
public class Function
{
    private readonly IPedidoRepository _repositorio;

    public Function() : this(new DynamoPedidoRepository(
        new AmazonDynamoDBClient(),
        new InfraOptions { TabelaPedidos = Ambiente.Obter("TABELA_PEDIDOS") })) { }

    public Function(IPedidoRepository repositorio) => _repositorio = repositorio;

    public Task<SQSBatchResponse> FunctionHandler(SQSEvent lote, ILambdaContext context) =>
        ProcessadorLote.ProcessarAsync(lote, context.Logger, evento => evento.EventType switch
        {
            PagamentoAprovado.Tipo => AtualizarAsync(evento, evento.Dados<PagamentoAprovado>().PedidoId, p => p.Confirmar(), context.Logger),
            PagamentoRecusado.Tipo => AtualizarAsync(evento, evento.Dados<PagamentoRecusado>().PedidoId, p => p.Cancelar(), context.Logger),
            EstoqueIndisponivel.Tipo => AtualizarAsync(evento, evento.Dados<EstoqueIndisponivel>().PedidoId, p => p.Cancelar(), context.Logger),
            _ => ProcessadorLote.Ignorar(evento, context.Logger)
        });

    private async Task AtualizarAsync(EventoRecebido evento, string pedidoId, Action<Pedido> transicao, ILambdaLogger log)
    {
        var pedido = await _repositorio.ObterPorIdAsync(pedidoId)
            ?? throw new InvalidOperationException($"Pedido {pedidoId} não encontrado.");

        var anterior = pedido.Status;
        transicao(pedido); // As regras de transição (ex.: cancelado não confirma) ficam no Domain.
        await _repositorio.SalvarAsync(pedido);

        log.LogInformation($"[{evento.CorrelationId}] {evento.EventType}: {anterior} -> {pedido.Status}");
    }
}

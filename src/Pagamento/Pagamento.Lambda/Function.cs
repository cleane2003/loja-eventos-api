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

namespace Pagamento.Lambda;

// EstoqueReservado -> cobra o cliente e publica PagamentoAprovado ou PagamentoRecusado.
public class Function
{
    // Simulação do gateway de pagamento: acima deste valor, o "cartão" recusa.
    private const decimal LimiteAprovacao = 1000m;
    private const string Aprovado = "Aprovado";
    private const string Recusado = "Recusado";
    private static readonly CultureInfo Invariante = CultureInfo.InvariantCulture;

    private readonly IAmazonDynamoDB _dynamo;
    private readonly PublicadorSns _publicador;
    private readonly string _tabelaPagamentos = Ambiente.Obter("TABELA_PAGAMENTOS");
    private readonly string _topicoAprovado = Ambiente.Obter("TOPICO_PAGAMENTO_APROVADO");
    private readonly string _topicoRecusado = Ambiente.Obter("TOPICO_PAGAMENTO_RECUSADO");

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
            EstoqueReservado.Tipo => CobrarAsync(evento.CorrelationId, evento.Dados<EstoqueReservado>(), context.Logger),
            _ => ProcessadorLote.Ignorar(evento, context.Logger)
        });

    private async Task CobrarAsync(string correlationId, EstoqueReservado reserva, ILambdaLogger log)
    {
        var status = reserva.ValorTotal <= LimiteAprovacao ? Aprovado : Recusado;
        var motivo = status == Aprovado ? "" : "Limite do cartão excedido";

        try
        {
            // Num gateway real (Stripe, Adyen...) o PedidoId iria como chave de idempotência na cobrança,
            // para o próprio gateway recusar uma segunda cobrança do mesmo pedido.
            await _dynamo.PutItemAsync(new PutItemRequest
            {
                TableName = _tabelaPagamentos,
                Item = new Dictionary<string, AttributeValue>
                {
                    ["PedidoId"] = new() { S = reserva.PedidoId },
                    ["ClienteId"] = new() { S = reserva.ClienteId },
                    ["Valor"] = new() { N = reserva.ValorTotal.ToString(Invariante) },
                    ["Status"] = new() { S = status },
                    ["Motivo"] = new() { S = motivo },
                    ["ProcessadoEm"] = new() { S = DateTime.UtcNow.ToString("O") }
                },
                ConditionExpression = "attribute_not_exists(PedidoId)"
            });

            log.LogInformation($"[{correlationId}] Pagamento de R$ {reserva.ValorTotal.ToString("N2", Invariante)} {status.ToLowerInvariant()}.");
        }
        catch (ConditionalCheckFailedException)
        {
            // Mensagem repetida: o cliente NÃO pode ser cobrado de novo. Republica a decisão já tomada.
            var existente = (await _dynamo.GetItemAsync(new GetItemRequest
            {
                TableName = _tabelaPagamentos,
                Key = new Dictionary<string, AttributeValue> { ["PedidoId"] = new() { S = reserva.PedidoId } },
                ConsistentRead = true
            })).Item;

            status = existente["Status"].S;
            motivo = existente["Motivo"].S;
            log.LogInformation($"[{correlationId}] Pagamento já processado ({status}), republicando sem cobrar de novo.");
        }

        if (status == Aprovado)
            await _publicador.PublicarAsync(_topicoAprovado, PagamentoAprovado.Tipo, correlationId,
                new PagamentoAprovado(reserva.PedidoId, reserva.ValorTotal));
        else
            await _publicador.PublicarAsync(_topicoRecusado, PagamentoRecusado.Tipo, correlationId,
                new PagamentoRecusado(reserva.PedidoId, motivo));
    }
}

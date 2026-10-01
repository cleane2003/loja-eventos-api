using System.Text.Json;
using Amazon.SecurityToken;
using Amazon.SecurityToken.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Contracts;

namespace Mensageria;

// Publica eventos recebendo só o NOME do tópico. O ARN é montado em tempo de execução,
// então nenhum arquivo do repositório precisa conter o ID da conta AWS.
public sealed class PublicadorSns
{
    private readonly IAmazonSimpleNotificationService _sns;
    private readonly Lazy<Task<string>> _prefixoArn;

    public PublicadorSns(IAmazonSimpleNotificationService sns, IAmazonSecurityTokenService sts)
    {
        _sns = sns;
        // "Quem sou eu?": devolve a conta das credenciais em uso e não exige permissão nenhuma.
        // Feito uma vez só por instância (na Lambda, só no cold start).
        _prefixoArn = new Lazy<Task<string>>(async () =>
        {
            var identidade = await sts.GetCallerIdentityAsync(new GetCallerIdentityRequest());
            var regiao = sns.Config.RegionEndpoint?.SystemName ?? Ambiente.Obter("AWS_REGION");
            return $"arn:aws:sns:{regiao}:{identidade.Account}:";
        });
    }

    public Task PublicarAsync<TData>(string topico, string tipo, string correlationId, TData data) =>
        PublicarEnvelopeAsync(topico, EventEnvelope.Criar(tipo, correlationId, data));

    public async Task PublicarEnvelopeAsync(string topico, IEventEnvelope envelope)
    {
        await _sns.PublishAsync(new PublishRequest
        {
            TopicArn = await _prefixoArn.Value + topico,
            Message = JsonSerializer.Serialize(envelope, envelope.GetType(), EventoRecebido.Json),
            // Atributo fora do corpo: permite que uma assinatura filtre por tipo de evento.
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["eventType"] = new() { DataType = "String", StringValue = envelope.EventType }
            }
        });
    }
}

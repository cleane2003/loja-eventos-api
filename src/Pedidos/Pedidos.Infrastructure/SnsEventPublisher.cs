using Contracts;
using Mensageria;
using Pedidos.Domain;

namespace Pedidos.Infrastructure;

// Adaptador (hexagonal): implementa a porta IEventPublisher usando SNS.
// Trocar por EventBridge ou RabbitMQ seria escrever outro adaptador, sem tocar no Domain.
public sealed class SnsEventPublisher(PublicadorSns publicador, InfraOptions opcoes) : IEventPublisher
{
    public async Task PublicarAsync<TEvento>(TEvento evento, CancellationToken ct = default) where TEvento : class
    {
        if (evento is not IEventEnvelope envelope)
            throw new ArgumentException("Só é possível publicar eventos dentro de um EventEnvelope.", nameof(evento));

        if (!opcoes.Topicos.TryGetValue(envelope.EventType, out var topico))
            throw new InvalidOperationException($"Nenhum tópico SNS configurado para o evento '{envelope.EventType}'.");

        await publicador.PublicarEnvelopeAsync(topico, envelope);
    }
}

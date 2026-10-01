namespace Pedidos.Domain;

// Porta (hexagonal): o domínio só sabe que precisa publicar eventos.
// Se é SNS, EventBridge ou RabbitMQ é detalhe da Infrastructure.
public interface IEventPublisher
{
    Task PublicarAsync<TEvento>(TEvento evento, CancellationToken ct = default) where TEvento : class;
}

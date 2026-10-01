namespace Contracts;

// Envelope comum a todos os eventos do sistema.
// EventId: idempotência | Version: evoluir o contrato sem quebrar consumidores
// CorrelationId: rastrear o mesmo pedido nos logs de todos os serviços.
public sealed record EventEnvelope<TData>(
    Guid EventId,
    string EventType,
    int Version,
    DateTime OccurredAt,
    string CorrelationId,
    TData Data) : IEventEnvelope;

// Permite ler os metadados do envelope sem saber o tipo do Data (usado pelo publicador).
public interface IEventEnvelope
{
    Guid EventId { get; }
    string EventType { get; }
    string CorrelationId { get; }
}

public static class EventEnvelope
{
    public static EventEnvelope<TData> Criar<TData>(string eventType, string correlationId, TData data, int version = 1)
        => new(Guid.NewGuid(), eventType, version, DateTime.UtcNow, correlationId, data);
}

using System.Text.Json;
using Contracts;

namespace Mensageria;

// Evento lido de uma mensagem SQS que veio do SNS, com os metadados do envelope já extraídos.
public sealed class EventoRecebido
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly string _json;

    public string EventType { get; }
    public string CorrelationId { get; }

    private EventoRecebido(string json, string eventType, string correlationId)
    {
        _json = json;
        EventType = eventType;
        CorrelationId = correlationId;
    }

    public static EventoRecebido DeMensagemSqs(string corpo)
    {
        // O SNS embrulha o nosso evento: ele chega como texto no campo "Message".
        using var envelopeSns = JsonDocument.Parse(corpo);
        if (!envelopeSns.RootElement.TryGetProperty("Message", out var message) || message.ValueKind != JsonValueKind.String)
            throw new FormatException("A mensagem não veio de um tópico SNS.");

        var json = message.GetString()!;
        using var evento = JsonDocument.Parse(json);
        if (!evento.RootElement.TryGetProperty("eventType", out var tipo) ||
            !evento.RootElement.TryGetProperty("correlationId", out var correlationId))
            throw new FormatException("A mensagem está fora do formato EventEnvelope.");

        return new EventoRecebido(json, tipo.GetString()!, correlationId.GetString()!);
    }

    public TData Dados<TData>() where TData : class =>
        JsonSerializer.Deserialize<EventEnvelope<TData>>(_json, Json)?.Data
        ?? throw new FormatException($"O evento {EventType} veio sem dados.");
}

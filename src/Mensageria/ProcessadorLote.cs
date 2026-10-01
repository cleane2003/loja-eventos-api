using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;

namespace Mensageria;

public static class ProcessadorLote
{
    // Processa cada mensagem do lote e devolve só as que falharam (ReportBatchItemFailures).
    // As que falharam voltam para a fila e, depois do limite de tentativas, vão para a DLQ.
    // Uma mensagem ruim não obriga a reprocessar as boas do mesmo lote.
    public static async Task<SQSBatchResponse> ProcessarAsync(SQSEvent lote, ILambdaLogger log, Func<EventoRecebido, Task> tratar)
    {
        var falhas = new List<SQSBatchResponse.BatchItemFailure>();

        foreach (var mensagem in lote.Records)
        {
            try
            {
                await tratar(EventoRecebido.DeMensagemSqs(mensagem.Body));
            }
            catch (Exception ex)
            {
                log.LogError($"Falha na mensagem {mensagem.MessageId}: {ex.Message}");
                falhas.Add(new SQSBatchResponse.BatchItemFailure { ItemIdentifier = mensagem.MessageId });
            }
        }

        return new SQSBatchResponse { BatchItemFailures = falhas };
    }

    // Evento que este serviço não trata: registra e segue, sem mandar para a DLQ.
    public static Task Ignorar(EventoRecebido evento, ILambdaLogger log)
    {
        log.LogWarning($"[{evento.CorrelationId}] Evento {evento.EventType} ignorado: não é tratado por este serviço.");
        return Task.CompletedTask;
    }
}

namespace Pedidos.Domain;

// Porta (hexagonal): o domínio diz O QUE precisa. O COMO (DynamoDB) fica na Infrastructure.
public interface IPedidoRepository
{
    Task SalvarAsync(Pedido pedido, CancellationToken ct = default);
    Task<Pedido?> ObterPorIdAsync(string id, CancellationToken ct = default);
}

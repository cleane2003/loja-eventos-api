using Pedidos.Domain;

namespace Pedidos.Api;

// O que o cliente envia no POST /pedidos.
public sealed record CriarPedidoRequest(string ClienteId, List<ItemRequest>? Itens);

public sealed record ItemRequest(string ProdutoId, int Quantidade, decimal PrecoUnitario);

// O que a API devolve. Nunca a entidade de domínio direto: o contrato HTTP fica estável.
public sealed record PedidoResponse(
    string Id,
    string ClienteId,
    string Status,
    DateTime CriadoEm,
    decimal ValorTotal,
    IReadOnlyList<ItemRequest> Itens)
{
    public static PedidoResponse De(Pedido pedido) => new(
        pedido.Id,
        pedido.ClienteId,
        pedido.Status.ToString(),
        pedido.CriadoEm,
        pedido.ValorTotal,
        pedido.Itens.Select(i => new ItemRequest(i.ProdutoId, i.Quantidade, i.PrecoUnitario)).ToList());
}

namespace Pedidos.Domain;

// Objeto de valor (DDD): não tem Id próprio, é definido pelos seus valores.
// Só existe dentro de um Pedido.
public sealed record ItemPedido
{
    public string ProdutoId { get; }
    public int Quantidade { get; }
    public decimal PrecoUnitario { get; }
    public decimal Subtotal => Quantidade * PrecoUnitario;

    public ItemPedido(string produtoId, int quantidade, decimal precoUnitario)
    {
        if (string.IsNullOrWhiteSpace(produtoId))
            throw new DomainException("O item precisa ter um produto.");

        if (quantidade <= 0)
            throw new DomainException("A quantidade precisa ser maior que zero.");

        // Simplificação de estudo: em produção o preço viria do catálogo, nunca do cliente.
        if (precoUnitario <= 0)
            throw new DomainException("O preço precisa ser maior que zero.");

        ProdutoId = produtoId;
        Quantidade = quantidade;
        PrecoUnitario = precoUnitario;
    }
}

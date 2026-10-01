namespace Contracts;

// Contrato público: é o que os outros serviços enxergam.
// Não reaproveita as classes do Domain de propósito, para o domínio poder mudar sem quebrar ninguém.
public sealed record PedidoCriado(
    string PedidoId,
    string ClienteId,
    IReadOnlyList<ItemPedidoCriado> Itens,
    decimal ValorTotal)
{
    public const string Tipo = "PedidoCriado";
}

public sealed record ItemPedidoCriado(string ProdutoId, int Quantidade, decimal PrecoUnitario);

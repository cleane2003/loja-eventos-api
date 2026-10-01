namespace Contracts;

// Leva o que o próximo consumidor (Pagamento) precisa para cobrar, sem consultar o serviço de Pedidos.
public sealed record EstoqueReservado(string PedidoId, string ClienteId, decimal ValorTotal)
{
    public const string Tipo = "EstoqueReservado";
}

public sealed record EstoqueIndisponivel(string PedidoId, IReadOnlyList<string> ProdutosIndisponiveis)
{
    public const string Tipo = "EstoqueIndisponivel";
}

namespace Contracts;

public sealed record PagamentoAprovado(string PedidoId, decimal Valor)
{
    public const string Tipo = "PagamentoAprovado";
}

public sealed record PagamentoRecusado(string PedidoId, string Motivo)
{
    public const string Tipo = "PagamentoRecusado";
}

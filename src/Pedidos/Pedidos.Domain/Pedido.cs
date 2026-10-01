namespace Pedidos.Domain;

// Raiz do agregado (DDD): ninguém altera itens ou status sem passar por aqui,
// então é esta classe que garante as regras do pedido.
public sealed class Pedido
{
    private readonly List<ItemPedido> _itens;

    public string Id { get; }
    public string ClienteId { get; }
    public StatusPedido Status { get; private set; }
    public DateTime CriadoEm { get; }
    public IReadOnlyList<ItemPedido> Itens => _itens;
    public decimal ValorTotal => _itens.Sum(i => i.Subtotal);

    private Pedido(string id, string clienteId, List<ItemPedido> itens, StatusPedido status, DateTime criadoEm)
    {
        Id = id;
        ClienteId = clienteId;
        _itens = itens;
        Status = status;
        CriadoEm = criadoEm;
    }

    // Único jeito de criar um pedido novo: já nasce válido e Pendente.
    public static Pedido Criar(string clienteId, IEnumerable<ItemPedido> itens)
    {
        if (string.IsNullOrWhiteSpace(clienteId))
            throw new DomainException("O pedido precisa ter um cliente.");

        var lista = itens.ToList();
        if (lista.Count == 0)
            throw new DomainException("Um pedido precisa ter pelo menos um item.");

        return new Pedido(Guid.NewGuid().ToString(), clienteId, lista, StatusPedido.Pendente, DateTime.UtcNow);
    }

    // Usado pelo repositório para remontar um pedido que já existe no banco.
    public static Pedido Restaurar(string id, string clienteId, IEnumerable<ItemPedido> itens, StatusPedido status, DateTime criadoEm)
        => new(id, clienteId, itens.ToList(), status, criadoEm);

    public void Confirmar()
    {
        // Evento repetido (ex.: PagamentoAprovado chegou duas vezes): não faz nada. Idempotência.
        if (Status == StatusPedido.Confirmado)
            return;

        if (Status == StatusPedido.Cancelado)
            throw new DomainException("Um pedido cancelado não pode ser confirmado.");

        Status = StatusPedido.Confirmado;
    }

    // Vale para Pendente e Confirmado. Cancelar um confirmado exige compensação
    // (estornar pagamento, devolver estoque), feita pelos outros serviços ao receber PedidoCancelado.
    public void Cancelar()
    {
        if (Status == StatusPedido.Cancelado)
            return;

        Status = StatusPedido.Cancelado;
    }
}

namespace Pedidos.Domain;

// Erro de regra de negócio (ex.: pedido sem itens). A Api transforma isso em 400 Bad Request.
public sealed class DomainException(string mensagem) : Exception(mensagem);

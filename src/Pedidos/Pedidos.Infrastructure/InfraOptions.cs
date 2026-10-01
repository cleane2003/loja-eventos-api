namespace Pedidos.Infrastructure;

// Valores lidos da seção "Aws" do appsettings.json.
public sealed class InfraOptions
{
    public const string Secao = "Aws";

    public string TabelaPedidos { get; set; } = "Pedidos";

    // Tipo do evento -> nome do tópico SNS. Ex.: "PedidoCriado" -> "pedido-criado"
    public Dictionary<string, string> Topicos { get; set; } = new();
}

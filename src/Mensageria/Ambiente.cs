namespace Mensageria;

public static class Ambiente
{
    // Configuração da Lambda vem de variáveis de ambiente, definidas no deploy.
    public static string Obter(string nome) =>
        Environment.GetEnvironmentVariable(nome)
        ?? throw new InvalidOperationException($"Variável de ambiente {nome} não configurada.");
}

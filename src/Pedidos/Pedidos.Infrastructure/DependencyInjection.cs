using Amazon.DynamoDBv2;
using Amazon.SecurityToken;
using Amazon.SimpleNotificationService;
using Mensageria;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pedidos.Domain;

namespace Pedidos.Infrastructure;

public static class DependencyInjection
{
    // Único ponto onde as portas do Domain são ligadas às implementações da AWS.
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var opcoes = configuration.GetSection(InfraOptions.Secao).Get<InfraOptions>() ?? new InfraOptions();
        services.AddSingleton(opcoes);

        // Região vem do appsettings; credenciais vêm da cadeia padrão do SDK:
        // na máquina, o "aws configure"; no ECS ou na Lambda, a role do IAM. Nunca no código.
        services.AddDefaultAWSOptions(configuration.GetAWSOptions(InfraOptions.Secao));
        services.AddAWSService<IAmazonDynamoDB>();
        services.AddAWSService<IAmazonSimpleNotificationService>();
        services.AddAWSService<IAmazonSecurityTokenService>();
        services.AddSingleton<PublicadorSns>();

        services.AddScoped<IPedidoRepository, DynamoPedidoRepository>();
        services.AddScoped<IEventPublisher, SnsEventPublisher>();

        return services;
    }
}

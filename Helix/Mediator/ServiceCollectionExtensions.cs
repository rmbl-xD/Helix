using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Helix;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Helix and auto-discovers all handlers, behaviors, processors, notification handlers,
    /// stream handlers, and exception handlers from the given assemblies.
    /// </summary>
    public static IServiceCollection AddHelix(this IServiceCollection services, params Assembly[] assemblies)
    {
        if (assemblies.Length == 0)
            assemblies = [Assembly.GetCallingAssembly()];

        services.AddTransient<IHelix, DefaultHelix>();

        foreach (var assembly in assemblies)
        {
            ScanAndRegister(services, assembly, typeof(IRequestHandler<,>));
            ScanAndRegister(services, assembly, typeof(IPipelineBehavior<,>));
            ScanAndRegister(services, assembly, typeof(INotificationHandler<>));
            ScanAndRegister(services, assembly, typeof(IStreamRequestHandler<,>));
            ScanAndRegister(services, assembly, typeof(IRequestPreProcessor<>));
            ScanAndRegister(services, assembly, typeof(IRequestPostProcessor<,>));
            ScanAndRegister(services, assembly, typeof(IRequestExceptionHandler<,>));
            ScanAndRegister(services, assembly, typeof(IRequestExceptionAction<>));
            ScanAndRegister(services, assembly, typeof(ICommandValidator<>));
            ScanAndRegister(services, assembly, typeof(ICommandValidator<,>));
        }

        return services;
    }

    private static void ScanAndRegister(IServiceCollection services, Assembly assembly, Type openGenericType)
    {
        var types = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces()
                .Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericType));

        foreach (var type in types)
        {
            if (type.IsGenericTypeDefinition)
            {
                services.AddTransient(openGenericType, type);
            }
            else
            {
                foreach (var iface in type.GetInterfaces()
                    .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openGenericType))
                {
                    services.AddTransient(iface, type);
                }
            }
        }
    }
}

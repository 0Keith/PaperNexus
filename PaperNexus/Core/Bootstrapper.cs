using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Linq;
using System.Reflection;

namespace PaperNexus.Core;

/// <summary>
/// Implement this interface on a class to have it automatically registered as
/// <typeparamref name="TService"/> singleton via <see cref="Bootstrapper.AddServicesFrom"/>.
/// </summary>
public interface IAddSingleton<TService>
{
}

/// <summary>
/// Implement this interface on a hosted service class to have it registered as both a singleton
/// (accessible via <typeparamref name="TService"/>) and as an <see cref="IHostedService"/>
/// via <see cref="Bootstrapper.AddServicesFrom"/>.
/// </summary>
public interface IAddHostedSingleton<TService>
{
}

public static class Bootstrapper
{
    public static IServiceCollection AddServicesFrom(this IServiceCollection services, Assembly assembly)
    {
        var openSingleton = typeof(IAddSingleton<>);
        var openHosted = typeof(IAddHostedSingleton<>);

        foreach (var implType in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        {
            foreach (var iface in implType.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openSingleton))
            {
                var serviceType = iface.GetGenericArguments()[0];
                services.AddSingleton(serviceType, implType);
            }

            foreach (var iface in implType.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openHosted))
            {
                var serviceType = iface.GetGenericArguments()[0];
                services.AddSingleton(implType);
                services.AddSingleton(serviceType, sp => sp.GetRequiredService(implType));
                services.AddSingleton<IHostedService>(sp => (IHostedService)sp.GetRequiredService(implType));
            }
        }

        var jobTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(IScheduleScopedJob).IsAssignableFrom(t))
            .ToList();

        foreach (var jobType in jobTypes)
        {
            var addMethod = typeof(Bootstrapper)
                .GetMethod(nameof(AddScheduledJobHostedService), BindingFlags.Static | BindingFlags.NonPublic)
                .MakeGenericMethod(jobType);
            addMethod.Invoke(null, new object[] { services });
        }

        return services;
    }

    private static void AddScheduledJobHostedService<TJob>(IServiceCollection services)
        where TJob : IScheduleScopedJob
    {
        services.AddHostedService<ScheduledJobHostedService<TJob>>();
    }
}

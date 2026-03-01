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

public static class Bootstrapper
{
    public static IServiceCollection AddServicesFrom(this IServiceCollection services, Assembly assembly)
    {
        var openMarker = typeof(IAddSingleton<>);

        foreach (var implType in assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        {
            foreach (var iface in implType.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == openMarker))
            {
                var serviceType = iface.GetGenericArguments()[0];
                services.AddSingleton(serviceType, implType);
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

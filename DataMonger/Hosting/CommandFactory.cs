using System.Reflection;
using DataMonger.Common;
using Microsoft.Extensions.DependencyInjection;

namespace DataMonger.Hosting;

/// <summary>
/// Creates console commands. Each command's services (its <c>ILogger&lt;T&gt;</c>, the REST client, config helpers, ...)
/// come from the DI container; the per-site <see cref="CommandContext"/> is passed as a runtime argument through
/// <see cref="ActivatorUtilities.CreateInstance(IServiceProvider, Type, object[])"/>, so commands are never registered
/// in the container themselves.
/// </summary>
public sealed class CommandFactory(IServiceProvider services)
{
    /// <summary>Every concrete <see cref="IConsoleCommand"/> in the DataMonger assembly (the menu's source).</summary>
    public static IReadOnlyList<Type> DiscoverCommandTypes() =>
        typeof(CommandFactory).Assembly.GetTypes()
            .Where(t => typeof(IConsoleCommand).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToList();

    /// <summary>Creates <paramref name="commandType"/> for one site (or the local run) described by <paramref name="context"/>.</summary>
    public IConsoleCommand Create(Type commandType, CommandContext context) =>
        (IConsoleCommand)ActivatorUtilities.CreateInstance(services, commandType, context);

    /// <inheritdoc cref="Create(Type, CommandContext)"/>
    public T Create<T>(CommandContext context) where T : IConsoleCommand =>
        ActivatorUtilities.CreateInstance<T>(services, context);
}

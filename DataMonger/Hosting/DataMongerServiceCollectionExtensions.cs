using bleak.Api.Rest;
using DataMonger.Common;
using DataMonger.Synth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DataMonger.Hosting;

/// <summary>The app's service registrations, shared by <c>Program</c> and the container test.</summary>
public static class DataMongerServiceCollectionExtensions
{
    /// <summary>Name of the <see cref="HttpClient"/> the shared <see cref="RestClient"/> sends with.</summary>
    public const string RestHttpClientName = "DataMonger.Rest";

    /// <summary>
    /// Registers logging (only the JSON-lines file sink at <paramref name="logFilePath"/>), the shared REST client and the
    /// helpers commands take through their constructors. Every registration is a singleton: they are stateless or
    /// thread-safe, and a run is one process. Commands themselves are not registered; <see cref="CommandFactory"/>
    /// creates them with a per-site <see cref="CommandContext"/>.
    /// </summary>
    public static IServiceCollection AddDataMonger(this IServiceCollection services, string logFilePath)
    {
        services.AddLogging(logging =>
        {
            // The Generic Host adds console, debug and event-source providers. The console one would write to stdout and
            // change the console UX (the documented transcripts depend on it), so the file sink is the only provider.
            logging.ClearProviders();
            // Information unless the "Logging" section (bound by the host) says otherwise.
            logging.SetMinimumLevel(LogLevel.Information);
            // The host also adds TraceId/SpanId/ParentId scopes; keep the line shape to RunId/Command/SiteId.
            logging.Configure(options => options.ActivityTrackingOptions = ActivityTrackingOptions.None);
            // Factory registration: the container owns the provider and disposes it (drain + flush) with the host.
            logging.Services.AddSingleton<ILoggerProvider>(_ => new JsonLinesFileLoggerProvider(logFilePath));
        });

        // RestClient is a singleton that keeps its HttpClient for the whole run, so the handler is not rotated by the
        // factory; SocketsHttpHandler's PooledConnectionLifetime picks up DNS changes instead. The factory's own
        // per-request logging is removed: RestClient already logs one Debug line per call (URL path only).
        services.AddHttpClient(RestHttpClientName)
            .UseSocketsHttpHandler((handler, _) => handler.PooledConnectionLifetime = TimeSpan.FromMinutes(2))
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .RemoveAllLoggers();
        services.AddSingleton<JsonSerializer>();
        services.AddSingleton(sp =>
        {
            var serializer = sp.GetRequiredService<JsonSerializer>();
            return new RestClient(
                sp.GetRequiredService<IHttpClientFactory>().CreateClient(RestHttpClientName),
                serializer,
                serializer,
                sp.GetRequiredService<ILogger<RestClient>>());
        });

        // ConfigFolders has optional console-stream parameters (for tests); the app uses Console.In/Out.
        services.AddSingleton(sp => new ConfigFolders(logger: sp.GetRequiredService<ILogger<ConfigFolders>>()));
        services.AddSingleton<CsvLoader>();
        services.AddSingleton<CommandConfigFile>();
        services.AddSingleton<SynthConfigLoader>();
        services.AddSingleton<CommandFactory>();
        return services;
    }
}

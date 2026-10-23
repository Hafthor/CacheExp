using System.Reflection;
using System.Runtime.CompilerServices;
using CacheExp;

var cachedWeatherProviders = Assembly.GetExecutingAssembly().ConstructedTypesOf<ICachedWeather>();
foreach (var cachedWeatherProvider in cachedWeatherProviders) {
    Dump(cachedWeatherProvider, cachedWeatherProvider.GetWeatherCached("London"));
    if (cachedWeatherProvider is ICachedWeatherAsync cachedWeatherAsyncProvider)
        Dump(cachedWeatherProvider, await cachedWeatherAsyncProvider.GetWeatherCachedAsync("London"));
    if (cachedWeatherProvider is ICachedWeatherValueAsync cachedWeatherValueAsyncProvider)
        Dump(cachedWeatherProvider, await cachedWeatherValueAsyncProvider.GetWeatherCachedAsync("London"));
}

return;

static void Dump(ICachedWeather provider, string message, [CallerArgumentExpression(nameof(message))] string expression = null) =>
    Console.WriteLine($"{provider.GetType().Name} - {expression}: {message}");
using System.Reflection;

namespace CacheExp;

public static class TypeLoaderExtensions {
    public static IEnumerable<Type> LoadableTypes(this Assembly assembly) {
        ArgumentNullException.ThrowIfNull(assembly);
        try {
            return assembly.GetTypes();
        } catch (ReflectionTypeLoadException e) {
            return e.Types.Where(t => t is not null);
        }
    }

    public static IEnumerable<Type> ImplementorsOf<T>(this IEnumerable<Type> types) where T : class {
        var t = typeof(T);
        return types.Where(p => t.IsAssignableFrom(p) && !p.IsInterface && !p.IsAbstract);
    }

    public static IEnumerable<T> Constructed<T>(this IEnumerable<Type> types) where T : class =>
        types.Select(t => (T)t.GetConstructor(Array.Empty<Type>())?.Invoke(Array.Empty<object>()))
            .Where(t => t is not null);

    public static IEnumerable<Type> LoadableTypesOf<T>(this Assembly assembly) {
        ArgumentNullException.ThrowIfNull(assembly);
        var t = typeof(T);
        return assembly.LoadableTypes().Where(p => t.IsAssignableFrom(p) && !p.IsInterface);
    }

    public static IEnumerable<T> ConstructedTypesOf<T>(this Assembly assembly) where T : class =>
        assembly.LoadableTypesOf<T>().Constructed<T>();
}
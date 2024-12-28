using Raffinert.Proj;

public static class Enumerable
{
    public static IEnumerable<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Proj<TSource, TResult> projection)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (projection == null) throw new ArgumentNullException(nameof(projection));

        return source.Select(projection.Map);
    }
}
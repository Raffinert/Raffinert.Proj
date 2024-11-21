using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Raffinert.Proj.DebugHelpers;

namespace Raffinert.Proj;

[DebuggerDisplay("{GetExpression()}")]
[DebuggerTypeProxy(typeof(ProjDebugView))]
public abstract class Proj<TIn, TOut> : IProj
{
    public abstract Expression<Func<TIn, TOut>> GetExpression();

    LambdaExpression IProj.GetExpression() => GetExpression();

    public static Proj<TIn, TOut> Create(Expression<Func<TIn, TOut>> expression)
    {
        return new InlineProj<TIn, TOut>(expression);
    }

    public virtual TOut Map(TIn candidate) => GetCompiledExpression()(candidate);

    private Func<TIn, TOut>? _compiledExpression;
    private Func<TIn, TOut> GetCompiledExpression()
    {
        return _compiledExpression ??= GetExpression().Compile();
    }
}

internal interface IProj
{
    LambdaExpression GetExpression();
}

public static class Queryable
{
    public static IQueryable<TResult> Select<TSource, TResult>(this IQueryable<TSource> source, Proj<TSource, TResult> projection)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (projection == null) throw new ArgumentNullException(nameof(projection));

        return source.Select(projection.GetExpression());
    }
}

public static class Enumerable
{
    public static IEnumerable<TResult> Select<TSource, TResult>(this IEnumerable<TSource> source, Proj<TSource, TResult> projection)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (projection == null) throw new ArgumentNullException(nameof(projection));

        return source.Select(projection.Map);
    }
}

file sealed class InlineProj<TIn, TOut>(Expression<Func<TIn, TOut>> expression) : Proj<TIn, TOut>
{
    public override Expression<Func<TIn, TOut>> GetExpression()
    {
        return (Expression<Func<TIn, TOut>>)new MapCallVisitor().Visit(expression)!;
    }
}

file sealed class RebindParameterVisitor(ParameterExpression oldParameter, Expression newParameter)
    : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node)
    {
        return node == oldParameter
            ? newParameter
            : base.VisitParameter(node);
    }
}

file sealed class MapCallVisitor : ExpressionVisitor
{
    protected override Expression VisitUnary(UnaryExpression node)
    {
        if (node.NodeType != ExpressionType.Convert) return base.VisitUnary(node);

        MemberExpression? memberExpr = null;
        NewExpression? newExpr = null;

        if (node.Operand is not MethodCallExpression mcx
            || mcx.Method.Name != nameof(Delegate.CreateDelegate)
            || mcx.Arguments.Count != 2)
        {
            return base.VisitUnary(node);
        }

        switch (mcx.Arguments[1])
        {
            case MemberExpression mex:
                memberExpr = mex;
                break;
            case NewExpression nex:
                newExpr = nex;
                break;
            default:
                return base.VisitUnary(node);
        }

        if (newExpr != null && !typeof(IProj).IsAssignableFrom(newExpr.Type))
        {
            return base.VisitUnary(node);
        }

        object value;

        if (newExpr != null)
        {
            value = Expression.Lambda(newExpr).Compile().DynamicInvoke();
        }
        else if (memberExpr is { Expression: ConstantExpression constantExpression, Member: FieldInfo fieldInfo })
        {
            var container = constantExpression.Value;
            value = fieldInfo.GetValue(container);
        }
        else
        {
            return base.VisitUnary(node);
        }

        if (!typeof(IProj).IsAssignableFrom(value.GetType()))
        {
            return base.VisitUnary(node);
        }

        var projExpression = ((IProj)value).GetExpression();

        return projExpression;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        if (node.Method.DeclaringType?.IsGenericType != true
            || node.Method.DeclaringType?.GetGenericTypeDefinition() != typeof(Proj<,>)
            || node.Method.Name != nameof(Proj<object, object>.Map)
            || node.Object is not MemberExpression memberExpr)
        {
            return base.VisitMethodCall(node);
        }

        if (memberExpr is not { Expression: ConstantExpression constantExpression, Member: FieldInfo fieldInfo })
        {
            return base.VisitMethodCall(node);
        }

        var container = constantExpression.Value;
        var value = fieldInfo.GetValue(container);

        if (!typeof(IProj).IsAssignableFrom(value.GetType()))
        {
            return base.VisitMethodCall(node);
        }

        var projExpression = ((IProj)value).GetExpression();

        var paramReplacer = new RebindParameterVisitor(projExpression.Parameters[0], node.Arguments[0]);

        var result = paramReplacer.Visit(projExpression.Body)!;

        return result;
    }
}
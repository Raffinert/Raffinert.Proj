using Raffinert.Proj.DebugHelpers;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace Raffinert.Proj;

[DebuggerDisplay("{GetExpandedExpression()}")]
[DebuggerTypeProxy(typeof(ProjDebugView))]
public abstract class Proj<TIn, TOut> : IProj
{
    public abstract Expression<Func<TIn, TOut>> GetExpression();

    LambdaExpression IProj.GetExpression() => GetExpression();
    LambdaExpression IProj.GetExpandedExpression() => GetExpandedExpression();
    LambdaExpression IProj.GetMapToExistingExpression() => GetMapToExistingExpression();

    public static Proj<TIn, TOut> Create(Expression<Func<TIn, TOut>> expression)
    {
        return new InlineProj<TIn, TOut>(expression);
    }

    public Proj<TIn, TOut> MergeBindings(Proj<TIn, TOut> other)
    {
        return new MergedProj<TIn, TOut>(this, other);
    }

    public Proj<TIn, TOut> MergeBindings(Expression<Func<TIn, TOut>> other)
    {
        return MergeBindings(new InlineProj<TIn, TOut>(other));
    }

    public TOut Map(TIn candidate) => GetCompiledExpression()(candidate);

    public TOut? MapIfNotNull(TIn? candidate) => candidate == null ? default : Map(candidate);

    public void MapToExisting(TIn source, ref TOut? dest)
    {
        if (dest == null)
        {
            dest = Map(source);
            return;
        }

        var mapAction = GetMapToExistingAction();
        mapAction(source, dest);
    }

    private Func<TIn, TOut>? _compiledExpression;
    private Func<TIn, TOut> GetCompiledExpression()
    {
        return _compiledExpression ??= GetExpression().Compile();
    }

    private Expression<Func<TIn, TOut>>? _expandedExpression;

    public Expression<Func<TIn, TOut>> GetExpandedExpression()
    {
        return _expandedExpression ??= (Expression<Func<TIn, TOut>>)new MapCallVisitor().Visit(GetExpression())!;
    }

    private Expression<Action<TIn, TOut>>? _mapToExistingExpression;

    public Expression<Action<TIn, TOut>> GetMapToExistingExpression()
    {
        if (_mapToExistingExpression != null)
        {
            return _mapToExistingExpression;

        }
        var expandedExpression = GetExpandedExpression();
        return _mapToExistingExpression = LambdaUpdater.RewriteToUpdateAction(expandedExpression);
    }

    private Action<TIn, TOut>? _mapToExistingAction;

    public Action<TIn, TOut> GetMapToExistingAction()
    {
        if (_mapToExistingAction != null)
        {
            return _mapToExistingAction;

        }
        var mapToExistingExpression = GetMapToExistingExpression();
        return _mapToExistingAction = mapToExistingExpression.Compile();
    }
}

internal interface IProj
{
    LambdaExpression GetExpression();
    LambdaExpression GetExpandedExpression();
    LambdaExpression GetMapToExistingExpression();
}

file sealed class UpdateInstanceVisitor(Expression existingInstance) : ExpressionVisitor
{
    protected override Expression VisitMemberInit(MemberInitExpression node)
    {
        var bindings = new List<Expression>();

        foreach (var binding in node.Bindings)
        {
            if (binding is MemberAssignment assignment)
            {
                var member = assignment.Member;
                var targetMember = Expression.PropertyOrField(existingInstance, member.Name);

                if (assignment.Expression is ConditionalExpression conditional)
                {
                    var trueBranch = CreateAssignExpression(targetMember, conditional.IfTrue);
                    var falseBranch = CreateAssignExpression(targetMember, conditional.IfFalse);

                    var conditionalBlock = Expression.IfThenElse(
                        conditional.Test,
                        trueBranch,
                        falseBranch
                    );

                    bindings.Add(conditionalBlock);
                }
                else if (assignment.Expression is MemberInitExpression nestedMemberInit)
                {
                    var nestedVisitor = new UpdateInstanceVisitor(targetMember);
                    var nestedAssignments = nestedVisitor.VisitMemberInit(nestedMemberInit);
                    bindings.Add(nestedAssignments);
                }
                else
                {
                    bindings.Add(Expression.Assign(targetMember, assignment.Expression));
                }
            }
            else
            {
                throw new NotSupportedException("Only MemberAssignment bindings are supported.");
            }
        }

        return Expression.Block(bindings);
    }

    protected override Expression VisitNew(NewExpression node)
    {
        // Skip the creation of a new instance
        return Expression.Empty();
    }

    private static Expression CreateAssignExpression(Expression targetMember, Expression sourceExpression)
    {
        if (sourceExpression is MemberInitExpression nestedMemberInit)
        {
            var nestedVisitor = new UpdateInstanceVisitor(targetMember);
            return nestedVisitor.VisitMemberInit(nestedMemberInit);
        }

        // Handle simple expressions or null
        return Expression.Assign(targetMember, sourceExpression);
    }
}

file static class LambdaUpdater
{
    public static Expression<Action<TIn, TOut>> RewriteToUpdateAction<TIn, TOut>(
        Expression<Func<TIn, TOut>> createExpression)
    {
        if (createExpression == null)
            throw new ArgumentNullException(nameof(createExpression));

        var sourceParameter = createExpression.Parameters[0];

        var uniqueExistingParamName = sourceParameter.Name == "existing" ? "existing1" : "existing";
        var existingInstance = Expression.Parameter(typeof(TOut), uniqueExistingParamName);

        var visitor = new UpdateInstanceVisitor(existingInstance);

        var updatedBody = visitor.Visit(createExpression.Body);

        return Expression.Lambda<Action<TIn, TOut>>(
            updatedBody!,
            sourceParameter,
            existingInstance
        );
    }
}

file sealed class MergedProj<TIn, TOut>(Proj<TIn, TOut> first, Proj<TIn, TOut> second) : Proj<TIn, TOut>
{
    public override Expression<Func<TIn, TOut>> GetExpression()
    {
        var firstExpression = first.GetExpandedExpression();
        var firstBindings = ExtractMemberBindings(firstExpression);
        var firstExpressionParameter = firstExpression.Parameters[0];
        var secondExpression = second.GetExpandedExpression();
        var paramReplacer = new RebindParameterVisitor(secondExpression.Parameters[0], firstExpressionParameter);
        var replacedExpr = (Expression<Func<TIn, TOut>>)paramReplacer.Visit(secondExpression)!;
        var secondBindings = ExtractMemberBindings(replacedExpr);
        var allBindings = firstBindings.Concat(secondBindings).ToList();
        var newBody = Expression.MemberInit(Expression.New(typeof(TOut)), allBindings);
        return Expression.Lambda<Func<TIn, TOut>>(newBody, firstExpressionParameter);
    }

    private static IEnumerable<MemberBinding> ExtractMemberBindings(Expression<Func<TIn, TOut>> expression)
    {
        return expression.Body switch
        {
            MemberInitExpression memberInit => memberInit.Bindings,
            ConditionalExpression conditionalExpression => HandleConditionalBindings(conditionalExpression),
            _ => throw new InvalidOperationException("Expression must be a MemberInitExpression or a ConditionalExpression.")
        };
    }

    private static IEnumerable<MemberBinding> HandleConditionalBindings(ConditionalExpression conditional)
    {
        if (conditional.IfTrue is MemberInitExpression trueInit && IsDefaultOrNullExpression(conditional.IfFalse))
        {
            // Handle p == null ? null : new FlatProduct { ... } and p != null ? new FlatProduct { ... } : default
            return trueInit.Bindings.Select(binding =>
            {
                if (binding is not MemberAssignment trueAssign)
                {
                    throw new InvalidOperationException("Unsupported binding type in conditional expression.");
                }

                var conditionalExpression = Expression.Condition(
                    conditional.Test,
                    trueAssign.Expression,
                    Expression.Default(trueAssign.Expression.Type)
                );

                return Expression.Bind(trueAssign.Member, conditionalExpression);
            });
        }

        if (conditional.IfFalse is MemberInitExpression falseInit && IsDefaultOrNullExpression(conditional.IfTrue))
        {
            return falseInit.Bindings.Select(binding =>
            {
                if (binding is not MemberAssignment falseAssign)
                {
                    throw new InvalidOperationException("Unsupported binding type in conditional expression.");
                }

                var conditionalExpression = Expression.Condition(
                    conditional.Test,
                    Expression.Default(falseAssign.Expression.Type),
                    falseAssign.Expression
                );

                return Expression.Bind(falseAssign.Member, conditionalExpression);
            });
        }

        var trueBindings = new List<MemberBinding>();
        var falseBindings = new List<MemberBinding>();

        if (conditional.IfTrue is MemberInitExpression trueInitOther)
        {
            trueBindings.AddRange(trueInitOther.Bindings);
        }

        if (conditional.IfFalse is MemberInitExpression falseInitOther)
        {
            falseBindings.AddRange(falseInitOther.Bindings);
        }

        var allBindings = trueBindings.Zip(falseBindings, (trueBinding, falseBinding) =>
        {
            if (trueBinding is not MemberAssignment trueAssign || falseBinding is not MemberAssignment falseAssign)
            {
                throw new InvalidOperationException("Unsupported binding type in conditional expression.");
            }

            var conditionalExpression = Expression.Condition(
                conditional.Test,
                trueAssign.Expression,
                falseAssign.Expression
            );

            return Expression.Bind(trueAssign.Member, conditionalExpression);

        });

        return allBindings;
    }

    private static bool IsDefaultOrNullExpression(Expression expression)
    {
        return expression is ConstantExpression { Value: null } or DefaultExpression;
    }
}


file sealed class InlineProj<TIn, TOut>(Expression<Func<TIn, TOut>> expression) : Proj<TIn, TOut>
{
    public override Expression<Func<TIn, TOut>> GetExpression()
    {
        return expression;
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

        if (node.Operand is not MethodCallExpression mcx
            || mcx.Method.Name != nameof(Delegate.CreateDelegate)
            || mcx.Arguments.Count != 2
            || GetInnerExpression(mcx.Arguments[1]) is not { } innerProjExpression)
        {
            return base.VisitUnary(node);
        }

        return innerProjExpression;
    }

    protected override Expression VisitMethodCall(MethodCallExpression node)
    {
        var isMapIfNotNullMethod = node.Method.Name == nameof(Proj<object, object>.MapIfNotNull);

        if (node.Method.DeclaringType?.IsGenericType != true
            || node.Method.DeclaringType?.GetGenericTypeDefinition() != typeof(Proj<,>)
            || node.Method.Name != nameof(Proj<object, object>.Map) && !isMapIfNotNullMethod
            || GetInnerExpression(node.Object) is not { } innerProjExpression)
        {
            return base.VisitMethodCall(node);
        }

        var paramReplacer = new RebindParameterVisitor(innerProjExpression.Parameters[0], node.Arguments[0]);

        if (!isMapIfNotNullMethod)
        {
            var result = paramReplacer.Visit(innerProjExpression.Body)!;
            return result;
        }

        var conditionalLambda = AddNullCheck(innerProjExpression);
        var conditionalResult = paramReplacer.Visit(conditionalLambda.Body)!;

        return conditionalResult;
    }


    private static LambdaExpression AddNullCheck(LambdaExpression expr)
    {
        if (expr.Parameters.Count != 1)
            throw new ArgumentException("Expression must have exactly one parameter.", nameof(expr));

        var parameter = expr.Parameters[0];
        var returnType = expr.ReturnType;
        var nullCheck = Expression.Equal(parameter, Expression.Constant(null, parameter.Type));
        var ifNull = Expression.Constant(GetDefault(returnType), returnType);
        var ifNotNull = expr.Body;
        var conditionalExpr = Expression.Condition(nullCheck, ifNull, ifNotNull);

        return Expression.Lambda(conditionalExpr, parameter);
    }

    private static object? GetDefault(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static LambdaExpression? GetInnerExpression(Expression? expression)
    {
        MemberExpression? memberExpr = null;
        NewExpression? newExpr = null;

        switch (expression)
        {
            case MemberExpression mex:
                memberExpr = mex;
                break;
            case NewExpression nex:
                newExpr = nex;
                break;
            default:
                return null;
        }

        if (newExpr != null && !typeof(IProj).IsAssignableFrom(newExpr.Type))
        {
            return null;
        }

        IProj? value;

        if (newExpr != null)
        {
            value = Expression.Lambda(newExpr).Compile().DynamicInvoke() as IProj;
        }
        else if (memberExpr is { Expression: ConstantExpression constantExpression, Member: FieldInfo fieldInfo })
        {
            var container = constantExpression.Value;
            value = fieldInfo.GetValue(container) as IProj;
        }
        else
        {
            return null;
        }

        return value?.GetExpandedExpression();
    }
}
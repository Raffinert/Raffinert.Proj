using System.Linq.Expressions;

namespace Raffinert.Proj.DebugHelpers;

internal class ProjDebugView(IProj proj)
{
    public LambdaExpression Expression => proj.GetExpression();
}
using System.Diagnostics;
using System.Linq.Expressions;

namespace Raffinert.Proj.DebugHelpers;

internal class ProjDebugView(IProj proj)
{
    public DebugViewInternal DebugView { get; } = new DebugViewInternal(proj);

    internal class DebugViewInternal(IProj proj)
    {
        [DebuggerBrowsable(DebuggerBrowsableState.Never)]
        private readonly IProj _proj = proj;

        public LambdaExpression Expression => _proj.GetExpression();
        public LambdaExpression ExpandedExpression => _proj.GetExpandedExpression();
    }
}
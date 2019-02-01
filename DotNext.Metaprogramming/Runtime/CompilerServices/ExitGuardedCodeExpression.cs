﻿using System;
using System.Linq.Expressions;

namespace DotNext.Runtime.CompilerServices
{
    using static Metaprogramming.Expressions;

    internal sealed class ExitGuardedCodeExpression: TransitionExpression
    {
        internal ExitGuardedCodeExpression(EnterGuardedCodeExpression enterCall)
            : base(enterCall.StateId - 1)
        {
        }

        public override Type Type => typeof(void);
        public override Expression Reduce() => Empty();
        internal override Expression Reduce(ParameterExpression stateMachine)
            => stateMachine.Call(nameof(AsyncStateMachine<ValueTuple>.ExitGuardedCode), StateId.AsConst());
    }
}

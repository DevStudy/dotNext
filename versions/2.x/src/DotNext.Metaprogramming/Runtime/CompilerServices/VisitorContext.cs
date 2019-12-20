using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace DotNext.Runtime.CompilerServices
{
    using static Collections.Generic.Stack;
    using AwaitExpression = Linq.Expressions.AwaitExpression;

    internal sealed class VisitorContext : Disposable
    {
        private static readonly UserDataSlot<StatePlaceholderExpression> StateIdPlaceholder = UserDataSlot<StatePlaceholderExpression>.Allocate();
        private readonly Stack<ExpressionAttributes> attributes;
        private readonly Stack<Statement> statements;
        private uint stateId;
        private uint previousStateId;

        internal VisitorContext(out LabelTarget asyncMethodEnd)
        {
            asyncMethodEnd = Expression.Label("end_async_method");
            attributes = new Stack<ExpressionAttributes>();
            statements = new Stack<Statement>();
            asyncMethodEnd.GetUserData().GetOrSet(StateIdPlaceholder).StateId = stateId = previousStateId = AsyncStateMachine<ValueTuple>.FINAL_STATE;
        }

        internal Statement CurrentStatement => statements.Peek();

        internal KeyValuePair<uint, StateTransition> NewTransition(IDictionary<uint, StateTransition> table)
        {
            var guardedStmt = FindStatement<GuardedStatement>();
            stateId += 1;
            var transition = new StateTransition(Expression.Label("state_" + stateId), guardedStmt?.FaultLabel);
            var pair = new KeyValuePair<uint, StateTransition>(stateId, transition);
            table.Add(pair);
            return pair;
        }

        private S? FindStatement<S>()
            where S : Statement
        {
            foreach (var statement in statements)
                if (statement is S result)
                    return result;
            return null;
        }

        internal bool IsInFinally => !(FindStatement<FinallyStatement>() is null);

        internal bool HasAwait
        {
            get
            {
                foreach (var attr in attributes)
                    if (ReferenceEquals(ExpressionAttributes.Get(CurrentStatement), attr))
                        break;
                    else if (attr.ContainsAwait)
                        return true;
                return false;
            }
        }

        internal ParameterExpression? ExceptionHolder => FindStatement<CatchStatement>()?.ExceptionVar;

        private void ContainsAwait()
        {
            foreach (var attr in attributes)
                if (ReferenceEquals(ExpressionAttributes.Get(CurrentStatement), attr))
                    return;
                else
                    attr.ContainsAwait = true;
        }

        private void AttachLabel(LabelTarget target)
        {
            if (target is null)
                return;
            ExpressionAttributes.Get(CurrentStatement)?.Labels.Add(target);
            target.GetUserData().GetOrSet(StateIdPlaceholder).StateId = stateId;
        }

        internal O Rewrite<I, O, A>(I expression, Converter<I, O> rewriter, Action<A>? initializer = null)
            where I : Expression
            where O : Expression
            where A : ExpressionAttributes, new()
        {
            var attr = new A() { StateId = stateId };
            initializer?.Invoke(attr);
            attr.AttachTo(expression);

            var isStatement = false;
            switch (expression)
            {
                case LabelExpression label:
                    AttachLabel(label.Target);
                    break;
                case GotoExpression @goto:
                    @goto.Target.GetUserData().GetOrSet(StateIdPlaceholder);
                    break;
                case LoopExpression loop:
                    AttachLabel(loop.ContinueLabel);
                    AttachLabel(loop.BreakLabel);
                    break;
                case Statement statement:
                    statements.Push(statement);
                    isStatement = true;
                    break;
                case AwaitExpression _:
                    attr.ContainsAwait = true;
                    ContainsAwait();
                    break;
            }
            attributes.Push(attr);
            var result = rewriter(expression);
            attributes.Pop().AttachTo(result);
            if (isStatement)
            {
                statements.Pop();
                previousStateId = attr.StateId;
            }
            return result;
        }

        internal O Rewrite<I, O>(I expression, Converter<I, O> rewriter)
            where I : Expression
            where O : Expression
            => Rewrite<I, O, ExpressionAttributes>(expression, rewriter);

        internal Expression Rewrite(TryExpression expression, IDictionary<uint, StateTransition> transitionTable, Converter<TryCatchFinallyStatement, Expression> rewriter)
        {
            var previousStateId = this.previousStateId;
            var statement = new TryCatchFinallyStatement(expression, transitionTable, previousStateId, ref stateId);
            return Rewrite<TryCatchFinallyStatement, Expression, ExpressionAttributes>(statement, rewriter, attributes => attributes.StateId = previousStateId);
        }

        internal IReadOnlyCollection<Expression> CreateJumpPrologue(GotoExpression @goto, ExpressionVisitor visitor)
        {
            var state = @goto.Target.GetUserData().GetOrSet(StateIdPlaceholder);
            var result = new LinkedList<Expression>();
            //iterate through snapshot of statements because collection can be modified
            var statements = this.statements.Clone();
            foreach (var lookup in statements)
                if (ExpressionAttributes.Get(lookup)?.Labels.Contains(@goto.Target) ?? false)
                    break;
                else if (lookup is TryCatchFinallyStatement statement)
                    result.AddLast(statement.InlineFinally(visitor, state));
            statements.Clear();
            return result;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                attributes.Clear();
                statements.Clear();
            }
        }
    }
}
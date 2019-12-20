using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DotNext.Metaprogramming
{
    using Linq.Expressions;

    /// <summary>
    /// Represents pattern matcher.
    /// </summary>
    public sealed class MatchBuilder : ExpressionBuilder<BlockExpression>
    {
        private delegate ConditionalExpression PatternMatch(LabelTarget endOfMatch);

        private interface ICaseStatementBuilder
        {
            Expression Build(ParameterExpression value);
        }

        internal abstract class MatchStatement<D> : Statement, ILexicalScope<MatchBuilder, D>
            where D : MulticastDelegate
        {
            [StructLayout(LayoutKind.Auto)]
            private protected readonly struct CaseStatementBuilder : ICaseStatementBuilder
            {
                private readonly Action<ParameterExpression> scope;
                private readonly MatchStatement<D> statement;

                internal CaseStatementBuilder(MatchStatement<D> statement, Action<ParameterExpression> scope)
                {
                    this.scope = scope;
                    this.statement = statement;
                }

                Expression ICaseStatementBuilder.Build(ParameterExpression value)
                {
                    scope(value);
                    return statement.Build();
                }
            }

            private readonly MatchBuilder builder;

            private protected MatchStatement(MatchBuilder builder) => this.builder = builder;

            private protected abstract MatchBuilder Build(MatchBuilder builder, D scope);

            public MatchBuilder Build(D scope) => Build(builder, scope);
        }

        private sealed class DefaultStatement : MatchStatement<Action<ParameterExpression>>
        {
            internal DefaultStatement(MatchBuilder builder)
                : base(builder)
            {
            }

            private protected override MatchBuilder Build(MatchBuilder builder, Action<ParameterExpression> scope)
            {
                scope(builder.value);
                return builder.Default(Build());
            }
        }

        private sealed class MatchByMemberStatement : MatchStatement<Action<MemberExpression>>
        {
            [StructLayout(LayoutKind.Auto)]
            private new readonly struct CaseStatementBuilder : ICaseStatementBuilder
            {
                private readonly string memberName;
                private readonly Action<MemberExpression> memberHandler;
                private readonly MatchByMemberStatement statement;

                internal CaseStatementBuilder(MatchByMemberStatement statement, string memberName, Action<MemberExpression> memberHandler)
                {
                    this.memberName = memberName;
                    this.memberHandler = memberHandler;
                    this.statement = statement;
                }

                Expression ICaseStatementBuilder.Build(ParameterExpression value)
                {
                    memberHandler(Expression.PropertyOrField(value, memberName));
                    return statement.Build();
                }
            }

            private readonly string memberName;
            private readonly Expression memberValue;

            internal MatchByMemberStatement(MatchBuilder builder, string memberName, Expression memberValue)
                : base(builder)
            {
                this.memberName = memberName;
                this.memberValue = memberValue;
            }

            private protected override MatchBuilder Build(MatchBuilder builder, Action<MemberExpression> scope)
            {
                var pattern = StructuralPattern(Sequence.Singleton((memberName, memberValue)));
                return builder.MatchByCondition(pattern, new CaseStatementBuilder(this, memberName, scope));
            }
        }

        private sealed class MatchByTwoMembersStatement : MatchStatement<Action<MemberExpression, MemberExpression>>
        {
            [StructLayout(LayoutKind.Auto)]
            private new readonly struct CaseStatementBuilder : ICaseStatementBuilder
            {
                private readonly string memberName1, memberName2;
                private readonly Action<MemberExpression, MemberExpression> memberHandler;
                private readonly MatchByTwoMembersStatement statement;

                internal CaseStatementBuilder(MatchByTwoMembersStatement statement, string memberName1, string memberName2, Action<MemberExpression, MemberExpression> memberHandler)
                {
                    this.memberName1 = memberName1;
                    this.memberName2 = memberName2;
                    this.memberHandler = memberHandler;
                    this.statement = statement;
                }

                Expression ICaseStatementBuilder.Build(ParameterExpression value)
                {
                    memberHandler(Expression.PropertyOrField(value, memberName1), Expression.PropertyOrField(value, memberName2));
                    return statement.Build();
                }
            }

            private readonly string memberName1, memberName2;
            private readonly Expression memberValue1, memberValue2;

            internal MatchByTwoMembersStatement(MatchBuilder builder, string memberName1, Expression memberValue1, string memberName2, Expression memberValue2)
                : base(builder)
            {
                this.memberName1 = memberName1;
                this.memberValue1 = memberValue1;
                this.memberName2 = memberName2;
                this.memberValue2 = memberValue2;
            }

            private protected override MatchBuilder Build(MatchBuilder builder, Action<MemberExpression, MemberExpression> scope)
            {
                var pattern = StructuralPattern(new[] { (memberName1, memberValue1), (memberName2, memberValue2) });
                return builder.MatchByCondition(pattern, new CaseStatementBuilder(this, memberName1, memberName2, scope));
            }
        }

        private sealed class MatchByThreeMembersStatement : MatchStatement<Action<MemberExpression, MemberExpression, MemberExpression>>
        {
            [StructLayout(LayoutKind.Auto)]
            private new readonly struct CaseStatementBuilder : ICaseStatementBuilder
            {
                private readonly string memberName1, memberName2, memberName3;
                private readonly Action<MemberExpression, MemberExpression, MemberExpression> memberHandler;
                private readonly MatchByThreeMembersStatement statement;

                internal CaseStatementBuilder(MatchByThreeMembersStatement statement, string memberName1, string memberName2, string memberName3, Action<MemberExpression, MemberExpression, MemberExpression> memberHandler)
                {
                    this.memberName1 = memberName1;
                    this.memberName2 = memberName2;
                    this.memberName3 = memberName3;
                    this.memberHandler = memberHandler;
                    this.statement = statement;
                }

                Expression ICaseStatementBuilder.Build(ParameterExpression value)
                {
                    memberHandler(Expression.PropertyOrField(value, memberName1), Expression.PropertyOrField(value, memberName2), Expression.PropertyOrField(value, memberName3));
                    return statement.Build();
                }
            }

            private readonly string memberName1, memberName2, memberName3;
            private readonly Expression memberValue1, memberValue2, memberValue3;

            internal MatchByThreeMembersStatement(MatchBuilder builder, string memberName1, Expression memberValue1, string memberName2, Expression memberValue2, string memberName3, Expression memberValue3)
                : base(builder)
            {
                this.memberName1 = memberName1;
                this.memberValue1 = memberValue1;
                this.memberName2 = memberName2;
                this.memberValue2 = memberValue2;
                this.memberName3 = memberName3;
                this.memberValue3 = memberValue3;
            }

            private protected override MatchBuilder Build(MatchBuilder builder, Action<MemberExpression, MemberExpression, MemberExpression> scope)
            {
                var pattern = StructuralPattern(new[] { (memberName1, memberValue1), (memberName2, memberValue2), (memberName3, memberValue3) });
                return builder.MatchByCondition(pattern, new CaseStatementBuilder(this, memberName1, memberName2, memberName3, scope));
            }
        }

        private sealed class MatchByConditionStatement : MatchStatement<Action<ParameterExpression>>
        {
            private readonly Pattern pattern;

            internal MatchByConditionStatement(MatchBuilder builder, Pattern pattern) : base(builder) => this.pattern = pattern;

            private protected override MatchBuilder Build(MatchBuilder builder, Action<ParameterExpression> scope)
                => builder.MatchByCondition(pattern, new CaseStatementBuilder(this, scope));
        }

        private class MatchByTypeStatement : MatchStatement<Action<ParameterExpression>>
        {
            private protected readonly Type expectedType;

            internal MatchByTypeStatement(MatchBuilder builder, Type expectedType) : base(builder) => this.expectedType = expectedType;

            private protected override MatchBuilder Build(MatchBuilder builder, Action<ParameterExpression> scope)
                => builder.MatchByType(expectedType, new CaseStatementBuilder(this, scope));
        }

        private sealed class MatchByTypeWithConditionStatement : MatchByTypeStatement
        {
            private readonly Pattern condition;

            internal MatchByTypeWithConditionStatement(MatchBuilder builder, Type expectedType, Pattern condition) : base(builder, expectedType) => this.condition = condition;

            private protected override MatchBuilder Build(MatchBuilder builder, Action<ParameterExpression> scope)
                => builder.MatchByType(expectedType, condition, new CaseStatementBuilder(this, scope));
        }

        /// <summary>
        /// Represents condition constructor for the switch case.
        /// </summary>
        /// <param name="value">The value participating in pattern match.</param>
        /// <returns>The condition for evaluation.</returns>
        public delegate Expression Pattern(ParameterExpression value);

        /// <summary>
        /// Represents constructor of the action to be executed if <paramref name="value"/>
        /// matches to the pattern defined by <see cref="Pattern"/>.
        /// </summary>
        /// <param name="value">The value participating in pattern match.</param>
        /// <returns>The action to be executed if object matches to the pattern.</returns>
        public delegate Expression CaseStatement(ParameterExpression value);

        [StructLayout(LayoutKind.Auto)]
        private readonly struct CaseStatementBuilder : ICaseStatementBuilder
        {
            private readonly CaseStatement statement;

            private CaseStatementBuilder(CaseStatement statement) => this.statement = statement;

            Expression ICaseStatementBuilder.Build(ParameterExpression value) => statement(value);

            internal static Expression Build(Expression result, ParameterExpression value) => result;

            public static implicit operator CaseStatementBuilder(CaseStatement statement) => new CaseStatementBuilder(statement);
        }

        private static readonly MethodInfo PlainCaseStatementBuilder = new Func<Expression, ParameterExpression, Expression>(CaseStatementBuilder.Build).Method;
        private readonly ParameterExpression value;
        private readonly BinaryExpression? assignment;
        private readonly ICollection<PatternMatch> patterns;
        private CaseStatement? defaultCase;

        internal MatchBuilder(Expression value, ILexicalScope currentScope)
            : base(currentScope)
        {
            patterns = new LinkedList<PatternMatch>();
            if (value is ParameterExpression param)
                this.value = param;
            else
            {
                this.value = Expression.Variable(value.Type);
                assignment = Expression.Assign(this.value, value);
            }
        }

        private static CaseStatement MakeCaseStatement(Expression value)
            => PlainCaseStatementBuilder.CreateDelegate<CaseStatement>(value);

        private static PatternMatch MatchByCondition<B>(ParameterExpression value, Pattern condition, B builder)
            where B : struct, ICaseStatementBuilder
        {
            var test = condition(value);
            var body = builder.Build(value);
            return endOfMatch => Expression.IfThen(test, endOfMatch.Goto(body));
        }

        private MatchBuilder MatchByCondition<B>(Pattern condition, B builder)
            where B : struct, ICaseStatementBuilder
        {
            patterns.Add(MatchByCondition(value, condition, builder));
            return this;
        }

        private static PatternMatch MatchByType<B>(ParameterExpression value, Type expectedType, B builder)
            where B : struct, ICaseStatementBuilder
        {
            var test = value.InstanceOf(expectedType);
            var typedValue = Expression.Variable(expectedType);
            var body = builder.Build(typedValue);
            return endOfMatch => Expression.IfThen(test, Expression.Block(Sequence.Singleton(typedValue), typedValue.Assign(value.Convert(expectedType)), endOfMatch.Goto(body)));
        }

        private MatchBuilder MatchByType<B>(Type expectedType, B builder)
            where B : struct, ICaseStatementBuilder
        {
            patterns.Add(MatchByType(value, expectedType, builder));
            return this;
        }

        private static PatternMatch MatchByType<B>(ParameterExpression value, Type expectedType, Pattern condition, B builder)
            where B : struct, ICaseStatementBuilder
        {
            var test = value.InstanceOf(expectedType);
            var typedVar = Expression.Variable(expectedType);
            var typedVarInit = typedVar.Assign(value.Convert(expectedType));
            var body = builder.Build(typedVar);
            var test2 = condition(typedVar);
            return endOfMatch => Expression.IfThen(test, Expression.Block(Sequence.Singleton(typedVar), typedVarInit, Expression.IfThen(test2, endOfMatch.Goto(body))));
        }

        private MatchBuilder MatchByType<B>(Type expectedType, Pattern condition, B builder)
            where B : struct, ICaseStatementBuilder
        {
            patterns.Add(MatchByType(value, expectedType, condition, builder));
            return this;
        }

        /// <summary>
        /// Defines pattern matching.
        /// </summary>
        /// <param name="pattern">The condition representing pattern.</param>
        /// <param name="body">The action to be executed if object matches to the pattern.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(Pattern pattern, CaseStatement body)
            => MatchByCondition<CaseStatementBuilder>(pattern, body);

        /// <summary>
        /// Defines pattern matching.
        /// </summary>
        /// <param name="pattern">The condition representing pattern.</param>
        /// <param name="value">The value to be supplied if the specified pattern matches to the passed object.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(Pattern pattern, Expression value) => Case(pattern, MakeCaseStatement(value));

        internal MatchStatement<Action<ParameterExpression>> Case(Pattern pattern) => new MatchByConditionStatement(this, pattern);

        internal MatchStatement<Action<MemberExpression>> Case(string memberName, Expression memberValue) => new MatchByMemberStatement(this, memberName, memberValue);

        internal MatchStatement<Action<MemberExpression, MemberExpression>> Case(string memberName1, Expression memberValue1, string memberName2, Expression memberValue2)
            => new MatchByTwoMembersStatement(this, memberName1, memberValue1, memberName2, memberValue2);

        internal MatchStatement<Action<MemberExpression, MemberExpression, MemberExpression>> Case(string memberName1, Expression memberValue1, string memberName2, Expression memberValue2, string memberName3, Expression memberValue3)
            => new MatchByThreeMembersStatement(this, memberName1, memberValue1, memberName2, memberValue2, memberName3, memberValue3);

        internal MatchStatement<Action<ParameterExpression>> Case(object structPattern) =>
            Case(StructuralPattern(GetProperties(structPattern)));

        /// <summary>
        /// Defines pattern matching based on the expected type of value.
        /// </summary>
        /// <remarks>
        /// This method equivalent to <c>case T value where condition(value): body();</c>
        /// </remarks>
        /// <param name="expectedType">The expected type of the value.</param>
        /// <param name="pattern">Additional condition associated with the value.</param>
        /// <param name="body">The action to be executed if object matches to the pattern.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(Type expectedType, Pattern pattern, CaseStatement body)
            => MatchByType<CaseStatementBuilder>(expectedType, pattern, body);

        internal MatchStatement<Action<ParameterExpression>> Case(Type expectedType, Pattern pattern) => new MatchByTypeWithConditionStatement(this, expectedType, pattern);

        /// <summary>
        /// Defines pattern matching based on the expected type of value.
        /// </summary>
        /// <remarks>
        /// This method equivalent to <c>case T value: body();</c>
        /// </remarks>
        /// <param name="expectedType">The expected type of the value.</param>
        /// <param name="body">The action to be executed if object matches to the pattern.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(Type expectedType, CaseStatement body)
            => MatchByType<CaseStatementBuilder>(expectedType, body);

        internal MatchStatement<Action<ParameterExpression>> Case(Type expectedType) => new MatchByTypeStatement(this, expectedType);

        /// <summary>
        /// Defines pattern matching based on the expected type of value.
        /// </summary>
        /// <typeparam name="T">The expected type of the value.</typeparam>
        /// <param name="body">The action to be executed if object matches to the pattern.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case<T>(CaseStatement body)
            => Case(typeof(T), body);

        private static Pattern StructuralPattern(IEnumerable<(string, Expression)> structPattern)
            => delegate (ParameterExpression obj)
            {
                var result = default(Expression);
                foreach (var (name, value) in structPattern)
                {
                    var element = Expression.PropertyOrField(obj, name).Equal(value);
                    result = result is null ? element : result.AndAlso(element);
                }
                return result ?? Expression.Constant(false);
            };

        private MatchBuilder Case(IEnumerable<(string, Expression)> structPattern, CaseStatement body)
            => Case(StructuralPattern(structPattern), body);

        /// <summary>
        /// Defines pattern matching based on structural matching.
        /// </summary>
        /// <param name="memberName">The name of the field or property.</param>
        /// <param name="memberValue">The expected value of the field or property.</param>
        /// <param name="body">The action to be executed if object matches to the pattern.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(string memberName, Expression memberValue, Func<MemberExpression, Expression> body)
            => Case(StructuralPattern(Sequence.Singleton((memberName, memberValue))), value => body(Expression.PropertyOrField(value, memberName)));



        /// <summary>
        /// Defines pattern matching based on structural matching.
        /// </summary>
        /// <param name="memberName1">The name of the first field or property.</param>
        /// <param name="memberValue1">The expected value of the first field or property.</param>
        /// <param name="memberName2">The name of the second field or property.</param>
        /// <param name="memberValue2">The expected value of the second field or property.</param>
        /// <param name="body">The action to be executed if object matches to the pattern.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(string memberName1, Expression memberValue1, string memberName2, Expression memberValue2, Func<MemberExpression, MemberExpression, Expression> body)
            => Case(StructuralPattern(new[] { (memberName1, memberValue1), (memberName2, memberValue2) }), value => body(Expression.PropertyOrField(value, memberName1), Expression.PropertyOrField(value, memberName2)));

        /// <summary>
        /// Defines pattern matching based on structural matching.
        /// </summary>
        /// <param name="memberName1">The name of the first field or property.</param>
        /// <param name="memberValue1">The expected value of the first field or property.</param>
        /// <param name="memberName2">The name of the second field or property.</param>
        /// <param name="memberValue2">The expected value of the second field or property.</param>
        /// <param name="memberName3">The name of the third field or property.</param>
        /// <param name="memberValue3">The expected value of the third field or property.</param>
        /// <param name="body">The action to be executed if object matches to the pattern.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(string memberName1, Expression memberValue1, string memberName2, Expression memberValue2, string memberName3, Expression memberValue3, Func<MemberExpression, MemberExpression, MemberExpression, Expression> body)
            => Case(StructuralPattern(new[] { (memberName1, memberValue1), (memberName2, memberValue2), (memberName3, memberValue3) }), value => body(Expression.PropertyOrField(value, memberName1), Expression.PropertyOrField(value, memberName2), Expression.PropertyOrField(value, memberName3)));

        private static (string, Expression) GetMemberPattern(object @this, string memberName, Type memberType, Func<object, object> valueProvider)
        {
            var value = valueProvider(@this);
            if (value is null)
                return (memberName, Expression.Default(memberType));
            else if (value is Expression expr)
                return (memberName, expr);
            else
                return (memberName, Expression.Constant(value, memberType));
        }

        private static IEnumerable<(string, Expression)> GetProperties(object structPattern)
        {
            const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.DeclaredOnly | BindingFlags.Instance;
            foreach (var property in structPattern.GetType().GetProperties(PublicInstance))
                yield return GetMemberPattern(structPattern, property.Name, property.PropertyType, property.GetValue);
            foreach (var field in structPattern.GetType().GetFields(PublicInstance))
                yield return GetMemberPattern(structPattern, field.Name, field.FieldType, field.GetValue);
        }

        /// <summary>
        /// Defines pattern matching based on structural matching.
        /// </summary>
        /// <param name="structPattern">The structure pattern represented by instance of anonymous type.</param>
        /// <param name="body">The action to be executed if object matches to the pattern.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(object structPattern, CaseStatement body)
            => Case(GetProperties(structPattern), body);

        /// <summary>
        /// Defines pattern matching based on structural matching.
        /// </summary>
        /// <param name="structPattern">The structure pattern represented by instance of anonymous type.</param>
        /// <param name="value">The value to be supplied if the specified structural pattern matches to the passed object.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Case(object structPattern, Expression value)
            => Case(structPattern, MakeCaseStatement(value));

        /// <summary>
        /// Defines default behavior in case when all defined patterns are false positive.
        /// </summary>
        /// <param name="body">The block of code to be evaluated as default case.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Default(CaseStatement body)
        {
            defaultCase = body;
            return this;
        }

        /// <summary>
        /// Defines default behavior in case when all defined patterns are false positive.
        /// </summary>
        /// <param name="value">The expression to be evaluated as default case.</param>
        /// <returns><c>this</c> builder.</returns>
        public MatchBuilder Default(Expression value) => Default(MakeCaseStatement(value));

        internal MatchStatement<Action<ParameterExpression>> Default() => new DefaultStatement(this);

        private protected override BlockExpression Build()
        {
            var endOfMatch = Expression.Label(Type, "end");
            //handle patterns
            ICollection<Expression> instructions = new LinkedList<Expression>();
            if (!(assignment is null))
                instructions.Add(assignment);
            foreach (var pattern in patterns)
                instructions.Add(pattern(endOfMatch));
            //handle default
            if (!(defaultCase is null))
                instructions.Add(Expression.Goto(endOfMatch, defaultCase(value)));
            //setup label as last instruction
            instructions.Add(Expression.Label(endOfMatch, Expression.Default(endOfMatch.Type)));
            return assignment is null ? Expression.Block(instructions) : Expression.Block(Sequence.Singleton(value), instructions);
        }

        private protected override void Cleanup()
        {
            patterns.Clear();
            defaultCase = null;
            base.Cleanup();
        }
    }
}
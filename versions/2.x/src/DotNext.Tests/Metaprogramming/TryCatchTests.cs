using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Xunit;

namespace DotNext.Metaprogramming
{
    using Linq.Expressions;
    using static CodeGenerator;
    using U = Linq.Expressions.UniversalExpression;

    [ExcludeFromCodeCoverage]
    public sealed class TryCatchTests : Assert
    {
        [Fact]
        public static void Fault()
        {
            var lambda = Lambda<Func<long, long, bool>>((fun, result) =>
            {
                var (arg1, arg2) = fun;
                Assign(result, true.Const());
                Try((U)arg1 / arg2)
                    .Fault(() => Assign(result, false.Const()))
                    .End();
            })
            .Compile();
            True(lambda(6, 3));
            Throws<DivideByZeroException>(() => lambda(6, 0));
        }

        [Fact]
        public static void Catch()
        {
            var lambda = Lambda<Func<long, long, bool>>((fun, result) =>
            {
                var (arg1, arg2) = fun;
                Assign(result, true.Const());
                Try((U)arg1 / arg2)
                    .Catch<DivideByZeroException>(() => Assign(result, false.Const()))
                    .End();
            })
            .Compile();

            True(lambda(6, 3));
            False(lambda(6, 0));
        }

        [Fact]
        public static void ReturnFromCatch()
        {
            var lambda = Lambda<Func<long, long, bool>>((fun, result) =>
            {
                var (arg1, arg2) = fun;
                Try((U)arg1 / arg2)
                    .Catch<DivideByZeroException>(() => Return(false.Const()))
                    .End();
                Return(true.Const());
            })
            .Compile();

            True(lambda(6, 3));
            False(lambda(6, 0));
        }

        [Fact]
        public static void CatchWithFilter()
        {
            var lambda = Lambda<Func<long, long, bool>>(fun =>
            {
                var (arg1, arg2) = fun;
                Try(Expression.Block((U)arg1 / arg2, true.Const()))
                    .Catch(typeof(Exception), e => e.InstanceOf<DivideByZeroException>(), e => InPlaceValue(false))
                    .OfType<bool>()
                    .End();
            })
            .Compile();

            True(lambda(6, 3));
            False(lambda(6, 0));
        }
    }
}
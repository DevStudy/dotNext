using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace DotNext
{
    using Reflection;
    using Runtime.CompilerServices;
    using Intrinsics = Runtime.Intrinsics;

    /// <summary>
    /// Generates hash code and equality check functions for the particular type.
    /// </summary>
    /// <typeparam name="T">The type for which equality check and hash code functions should be generated.</typeparam>
    /// <remarks>
    /// Automatically generated hash code and equality check functions can be used
    /// instead of manually written implementation of overridden <see cref="object.GetHashCode"/> and <see cref="object.Equals(object)"/> methods.
    /// </remarks>
    [RuntimeFeatures(RuntimeGenericInstantiation = true, DynamicCodeCompilation = true, PrivateReflection = true)]
    public struct EqualityComparerBuilder<T>
    {
        private const BindingFlags PublicStaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        private bool salted;
        private ICollection<string> excludedFields;

        /// <summary>
        /// Sets an array of excluded field names.
        /// </summary>
        /// <value>An array of excluded fields.</value>
        [SuppressMessage("Performance", "CA1819", Justification = "Property is write-only")]
        public string[] ExcludedFields
        {
            set => excludedFields = new HashSet<string>(value);
        }

        private bool IsIncluded(FieldInfo field) => excludedFields is null || !excludedFields.Contains(field.Name);

        /// <summary>
        /// Set a value indicating that hash code must be unique for each application instance.
        /// </summary>
        /// <value><see langword="true"/> to include randomized salt data into hashing; <see langword="false"/> to use data from memory only.</value>
        public bool SaltedHashCode
        {
            set => salted = value;
        }

        private sealed class ConstructedEqualityComparer : IEqualityComparer<T>
        {
            private readonly Func<T, T, bool> equality;
            private readonly Func<T, int> hashCode;

            internal ConstructedEqualityComparer(Func<T, T, bool> equality, Func<T, int> hashCode)
            {
                this.equality = equality;
                this.hashCode = hashCode;
            }

            bool IEqualityComparer<T>.Equals(T x, T y) => equality(x, y);

            int IEqualityComparer<T>.GetHashCode(T obj) => hashCode(obj);
        }

        private static MethodCallExpression EqualsMethodForValueType(MemberExpression first, MemberExpression second)
        {
            var method = typeof(BitwiseComparer<>)
                .MakeGenericType(first.Type)
                .GetMethod(nameof(BitwiseComparer<int>.Equals), BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .MakeGenericMethod(second.Type);
            return Expression.Call(method, first, second);
        }

        private static MethodCallExpression HashCodeMethodForValueType(Expression expr, ConstantExpression salted)
        {
            var method = typeof(BitwiseComparer<>)
                .MakeGenericType(expr.Type)
                .GetMethod(nameof(BitwiseComparer<int>.GetHashCode), new[] { expr.Type, typeof(bool) });
            return Expression.Call(method, expr, salted);
        }

        private static MethodInfo EqualsMethodForArrayElementType(Type itemType)
        {
            var arrayType = Type.MakeGenericMethodParameter(0).MakeArrayType();
            return itemType.IsValueType ?
                typeof(OneDimensionalArray)
                        .GetMethod(nameof(OneDimensionalArray.BitwiseEquals), 1, PublicStaticFlags, null, new[] { arrayType, arrayType }, null)!
                        .MakeGenericMethod(itemType)
                : new Func<IEnumerable<object>, IEnumerable<object>, bool>(Sequence.SequenceEqual).Method;
        }

        private static MethodCallExpression EqualsMethodForArrayElementType(MemberExpression fieldX, MemberExpression fieldY)
        {
            var method = EqualsMethodForArrayElementType(fieldX.Type.GetElementType());
            return Expression.Call(method, fieldX, fieldY);
        }

        private static MethodInfo HashCodeMethodForArrayElementType(Type itemType)
        {
            var arrayType = Type.MakeGenericMethodParameter(0).MakeArrayType();
            return itemType.IsValueType ?
                  typeof(OneDimensionalArray)
                          .GetMethod(nameof(OneDimensionalArray.BitwiseHashCode), 1, PublicStaticFlags, null, new[] { arrayType, typeof(bool) }, null)!
                          .MakeGenericMethod(itemType) :
                  typeof(Sequence)
                          .GetMethod(nameof(Sequence.SequenceHashCode), new[] { typeof(IEnumerable<object>), typeof(bool) });
        }

        private static MethodCallExpression HashCodeMethodForArrayElementType(Expression expr, ConstantExpression salted)
        {
            var method = HashCodeMethodForArrayElementType(expr.Type.GetElementType());
            return Expression.Call(method, expr, salted);
        }

        private static IEnumerable<FieldInfo> GetAllFields(Type type)
        {
            foreach (var t in type.GetBaseTypes(includeTopLevel: true))
                foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic))
                    yield return field;
        }

        private Func<T, T, bool> BuildEquals()
        {
            var x = Expression.Parameter(typeof(T));
            if (x.Type.IsPrimitive)
                return EqualityComparer<T>.Default.Equals;
            else if (x.Type.IsArray && x.Type.GetArrayRank() == 1)
                return EqualsMethodForArrayElementType(x.Type.GetElementType()).CreateDelegate<Func<T, T, bool>>();
            else
            {
                var y = Expression.Parameter(x.Type);
                //collect all fields in the hierarchy
                Expression? expr = x.Type.IsClass ? Expression.ReferenceNotEqual(y, Expression.Constant(null, y.Type)) : null;
                foreach (var field in GetAllFields(x.Type))
                    if (IsIncluded(field))
                    {
                        var fieldX = Expression.Field(x, field);
                        var fieldY = Expression.Field(y, field);
                        Expression condition;
                        if (field.FieldType.IsPointer || field.FieldType.IsPrimitive || field.FieldType.IsEnum)
                            condition = Expression.Equal(fieldX, fieldY);
                        else if (field.FieldType.IsValueType)
                            condition = EqualsMethodForValueType(fieldX, fieldY);
                        else if (field.FieldType.IsArray && field.FieldType.GetArrayRank() == 1)
                            condition = EqualsMethodForArrayElementType(fieldX, fieldY);
                        else
                            condition = Expression.Call(new Func<object, object, bool>(Equals).Method, fieldX, fieldY);
                        expr = expr is null ? condition : Expression.AndAlso(expr, condition);
                    }
                if (x.Type.IsClass)
                    expr = Expression.OrElse(Expression.ReferenceEqual(x, y), expr);
                return Expression.Lambda<Func<T, T, bool>>(expr, false, x, y).Compile();
            }
        }

        private Func<T, int> BuildGetHashCode()
        {
            Expression expr;
            var inputParam = Expression.Parameter(typeof(T));
            if (inputParam.Type.IsPrimitive)
                return EqualityComparer<T>.Default.GetHashCode;
            else if (inputParam.Type.IsArray && inputParam.Type.GetArrayRank() == 1)
            {
                expr = HashCodeMethodForArrayElementType(inputParam, Expression.Constant(salted));
                return Expression.Lambda<Func<T, int>>(expr, true, inputParam).Compile();
            }
            else
            {
                var hashCodeTemp = Expression.Parameter(typeof(int));
                ICollection<Expression> expressions = new LinkedList<Expression>();
                //collect all fields in the hierarchy
                foreach (var field in GetAllFields(inputParam.Type))
                    if (IsIncluded(field))
                    {
                        expr = Expression.Field(inputParam, field);
                        if (field.FieldType.IsPointer)
                            expr = Expression.Call(typeof(Intrinsics).GetMethod(nameof(Intrinsics.PointerHashCode), BindingFlags.Static | BindingFlags.DeclaredOnly | BindingFlags.NonPublic), expr);
                        else if (field.FieldType.IsPrimitive)
                            expr = Expression.Call(expr, nameof(GetHashCode), Array.Empty<Type>());
                        else if (field.FieldType.IsValueType)
                            expr = HashCodeMethodForValueType(expr, Expression.Constant(salted));
                        else if (field.FieldType.IsArray && field.FieldType.GetArrayRank() == 1)
                            expr = HashCodeMethodForArrayElementType(expr, Expression.Constant(salted));
                        else
                        {
                            expr = Expression.Condition(
                                Expression.ReferenceEqual(expr, Expression.Constant(null, expr.Type)),
                                Expression.Constant(0, typeof(int)),
                                Expression.Call(expr, nameof(GetHashCode), Array.Empty<Type>()));
                        }
                        expr = Expression.Assign(hashCodeTemp, Expression.Add(Expression.Multiply(hashCodeTemp, Expression.Constant(-1521134295)), expr));
                        expressions.Add(expr);
                    }
                expressions.Add(hashCodeTemp);
                expr = Expression.Block(typeof(int), Sequence.Singleton(hashCodeTemp), expressions);
                return Expression.Lambda<Func<T, int>>(expr, false, inputParam).Compile();
            }
        }

        /// <summary>
        /// Generates implementation of <see cref="object.GetHashCode"/> and <see cref="object.Equals(object)"/> methods
        /// for particular type <typeparamref name="T"/>.
        /// </summary>
        /// <param name="equals">The implementation of equality check.</param>
        /// <param name="hashCode">The implementation of hash code.</param>
        public void Build(out Func<T, T, bool> equals, out Func<T, int> hashCode)
        {
            equals = BuildEquals();
            hashCode = BuildGetHashCode();
        }

        /// <summary>
        /// Generates implementation of equality comparer.
        /// </summary>
        /// <returns>The generated equality comparer.</returns>
        public IEqualityComparer<T> Build()
            => typeof(T).IsPrimitive ? (IEqualityComparer<T>)EqualityComparer<T>.Default : new ConstructedEqualityComparer(BuildEquals(), BuildGetHashCode());
    }
}
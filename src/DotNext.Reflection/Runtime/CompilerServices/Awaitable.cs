﻿using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace DotNext.Runtime.CompilerServices
{
    using Reflection;

    /// <summary>
    /// Represents awaitable concept type.
    /// </summary>
    /// <typeparam name="T">The constrained type.</typeparam>
    /// <typeparam name="TAwaiter">The type constrained with concept <see cref="Awaiter{TAwaiter}"/>.</typeparam>
    /// <seealso href="https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap">TAP</seealso>
    [Concept]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Awaitable<T, [Constraint(typeof(Awaiter<>))] TAwaiter>
        where TAwaiter : ICriticalNotifyCompletion
    {
        private static readonly Operator<T, TAwaiter> getAwaiter = Type<T>.Method.Require<Operator<T, TAwaiter>>(nameof(Task.GetAwaiter), MethodLookup.Instance);

        static Awaitable() => Concept.Assert<Awaiter<TAwaiter>>();

        private readonly T awaitable;

        /// <summary>
        /// Wraps value of type <typeparamref name="T"/> into awaitable value compatible with <see langword="await"/> expression.
        /// </summary>
        /// <param name="awaitable">Underlying awaitable object.</param>
        public Awaitable(T awaitable) => this.awaitable = awaitable;

        /// <summary>
        /// Gets awaiter used to await asynchronous result represented by type <typeparamref name="T"/>.
        /// </summary>
        /// <returns>An awaiter instance.</returns>
        public Awaiter<TAwaiter> GetAwaiter() => new Awaiter<TAwaiter>(GetAwaiter(in awaitable));

        /// <summary>
        /// Gets underlying awaitable object.
        /// </summary>
        /// <param name="awaitable">Awaitable object container.</param>
        public static implicit operator T(in Awaitable<T, TAwaiter> awaitable) => awaitable.awaitable;

        /// <summary>
        /// Gets awaiter used to await asynchronous result represented by type <typeparamref name="T"/>.
        /// </summary>
        /// <param name="obj">The object representing asynchronous computing.</param>
        /// <returns>An awaiter instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TAwaiter GetAwaiter(in T obj) => getAwaiter(in obj);
    }

    /// <summary>
    /// Represents awaitable concept type for the task type with non-void result.
    /// </summary>
    /// <typeparam name="T">The constrained type.</typeparam>
    /// <typeparam name="TAwaiter">The type constrained with concept <see cref="Awaiter{TAwaiter}"/>.</typeparam>
    /// <typeparam name="R">The type of asynchronous result.</typeparam>
    /// <seealso href="https://docs.microsoft.com/en-us/dotnet/standard/asynchronous-programming-patterns/task-based-asynchronous-pattern-tap">TAP</seealso>
    [Concept]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Awaitable<T, [Constraint(typeof(Awaiter<,>))] TAwaiter, R>
        where TAwaiter : ICriticalNotifyCompletion
    {
        private static readonly Operator<T, TAwaiter> getAwaiter = Type<T>.Method.Require<Operator<T, TAwaiter>>(nameof(Task.GetAwaiter), MethodLookup.Instance);

        static Awaitable() => Concept.Assert<Awaiter<TAwaiter, R>>();

        private readonly T awaitable;

        /// <summary>
        /// Wraps value of type <typeparamref name="T"/> into awaitable value compatible with <see langword="await"/> expression.
        /// </summary>
        /// <param name="awaitable">Underlying awaitable object.</param>
        public Awaitable(T awaitable) => this.awaitable = awaitable;

        /// <summary>
        /// Gets awaiter used to await asynchronous result represented by type <typeparamref name="T"/>.
        /// </summary>
        /// <returns>An awaiter instance.</returns>
        public Awaiter<TAwaiter, R> GetAwaiter() => new Awaiter<TAwaiter, R>(GetAwaiter(in awaitable));

        /// <summary>
        /// Gets underlying awaitable object.
        /// </summary>
        /// <param name="awaitable">Awaitable object container.</param>
        public static implicit operator T(in Awaitable<T, TAwaiter, R> awaitable) => awaitable.awaitable;

        /// <summary>
        /// Gets awaiter used to await asynchronous result represented by type <typeparamref name="T"/>.
        /// </summary>
        /// <param name="obj">The object representing asynchronous computing.</param>
        /// <returns>An awaiter instance.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TAwaiter GetAwaiter(in T obj) => getAwaiter(in obj);
    }
}
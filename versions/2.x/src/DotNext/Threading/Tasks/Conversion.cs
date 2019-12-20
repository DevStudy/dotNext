﻿using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace DotNext.Threading.Tasks
{
    /// <summary>
    /// Provides task result conversion methods.
    /// </summary>
    public static class Conversion
    {
        private static class NullableConverter<T>
            where T : struct
        {
            internal static readonly Converter<T, T?> Value = value => new T?(value);
        }

        /// <summary>
        /// Converts one type of task into another.
        /// </summary>
        /// <typeparam name="I">The source task type.</typeparam>
        /// <typeparam name="O">The target task type.</typeparam>
        /// <param name="task">The task to convert.</param>
        /// <returns>The converted task.</returns>
        public static Task<O> Convert<I, O>(this Task<I> task) where I : O => task.Convert(Converter.Identity<I, O>());

        /// <summary>
        /// Converts one type of task into another.
        /// </summary>
        /// <typeparam name="I">The source task type.</typeparam>
        /// <typeparam name="O">The target task type.</typeparam>
        /// <param name="task">The task to convert.</param>
        /// <param name="converter">Non-blocking conversion function.</param>
        /// <returns>The converted task.</returns>
		public static async Task<O> Convert<I, O>(this Task<I> task, Converter<I, O> converter)
            => converter(await task.ConfigureAwait(false));

        /// <summary>
        /// Converts value type into nullable value type.
        /// </summary>
        /// <param name="task">The task to convert.</param>
        /// <typeparam name="T">The value type.</typeparam>
        /// <returns>The converted task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task<T?> ToNullable<T>(this Task<T> task)
            where T : struct
            => Convert(task, NullableConverter<T>.Value);

        /// <summary>
        /// Converts one type of task into another.
        /// </summary>
        /// <typeparam name="I">The source task type.</typeparam>
        /// <typeparam name="O">The target task type.</typeparam>
        /// <param name="task">The task to convert.</param>
        /// <param name="converter">Asynchronous conversion function.</param>
        /// <returns>The converted task.</returns>
        public static async Task<O> Convert<I, O>(this Task<I> task, Converter<I, Task<O>> converter)
            => await converter(await task.ConfigureAwait(false)).ConfigureAwait(false);

        /// <summary>
        /// Allows to convert <see cref="Task{TResult}"/> of unknown result type into dynamically
        /// typed task which result can be obtained as <see cref="object"/>
        /// or any other data type using <c>dynamic</c> approach.
        /// </summary>
        /// <remarks>
        /// The type of the returned task is not known at compile time and therefore treated as <c>dynamic</c>. The result value returned 
        /// by <c>await</c> operator is equal to <see cref="System.Reflection.Missing.Value"/> if <paramref name="task"/> is not of type <see cref="Task{TResult}"/>.
        /// </remarks>
        /// <param name="task">The arbitrary task of type <see cref="Task{TResult}"/>.</param>
        /// <returns>The dynamically typed task.</returns>
        public static DynamicTaskAwaitable AsDynamic(this Task task) => new DynamicTaskAwaitable(task);
    }
}

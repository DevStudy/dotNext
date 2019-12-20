using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace DotNext
{
    using static Reflection.TypeExtensions;

    /// <summary>
    /// Represents extension methods for type <see cref="Result{T}"/>.
    /// </summary>
    public static class Result
    {
        /// <summary>
        /// If a result is successful, returns it, otherwise <see langword="null"/>.
        /// </summary>
        /// <typeparam name="T">Value type.</typeparam>
        /// <param name="result">The result.</param>
        /// <returns>Nullable value.</returns>
        public static T? OrNull<T>(this in Result<T> result)
            where T : struct
            => result.TryGet(out var value) ? new T?(value) : null;

        /// <summary>
        /// Returns the second result if the first is unsuccessful.
        /// </summary>
        /// <param name="first">The first result.</param>
        /// <param name="second">The second result.</param>
        /// <typeparam name="T">The type of value.</typeparam>
        /// <returns>The second result if the first is unsuccessful; otherwise, the first result.</returns>
        public static ref readonly Result<T> Coalesce<T>(this in Result<T> first, in Result<T> second) => ref first.IsSuccessful ? ref first : ref second;

        /// <summary>
        /// Indicates that specified type is result type.
        /// </summary>
        /// <returns><see langword="true"/>, if specified type is result type; otherwise, <see langword="false"/>.</returns>
        public static bool IsResult(this Type resultType) => resultType.IsGenericInstanceOf(typeof(Result<>));

        /// <summary>
        /// Returns the underlying type argument of the specified result type.
        /// </summary>
        /// <param name="resultType">Result type.</param>
        /// <returns>Underlying type argument of result type; otherwise, <see langword="null"/>.</returns>
        public static Type? GetUnderlyingType(Type resultType) => IsResult(resultType) ? resultType.GetGenericArguments()[0] : null;
    }

    /// <summary>
    /// Represents a result of operation which can be actual result or exception.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Auto)]
    public readonly struct Result<T> : ISerializable
    {
        private const string ExceptionSerData = "Exception";
        private const string ValueSerData = "Value";

        [AllowNull]
        private readonly T value;
        private readonly ExceptionDispatchInfo? exception;

        /// <summary>
        /// Initializes a new successful result.
        /// </summary>
        /// <param name="value">The value to be stored as result.</param>
        public Result(T value)
        {
            if (value is Exception e)
            {
                exception = ExceptionDispatchInfo.Capture(e);
                this.value = default;
            }
            else
            {
                this.value = value;
                exception = null;
            }
        }

        /// <summary>
        /// Initializes a new unsuccessful result.
        /// </summary>
        /// <param name="error">The exception representing error. Cannot be <see langword="null"/>.</param>
        public Result(Exception error)
        {
            exception = ExceptionDispatchInfo.Capture(error);
            value = default;
        }

        private Result(ExceptionDispatchInfo dispatchInfo)
        {
            value = default;
            exception = dispatchInfo;
        }

        [SuppressMessage("Usage", "CA1801", Justification = "context is required by .NET serialization framework")]
        private Result(SerializationInfo info, StreamingContext context)
        {
            value = (T)info.GetValue(ValueSerData, typeof(T));
            exception = info.GetValue(ExceptionSerData, typeof(Exception)) is Exception e ?
                ExceptionDispatchInfo.Capture(e) :
                null;
        }

        /// <summary>
        /// Indicates that the result is successful.
        /// </summary>
        /// <value><see langword="true"/> if this result is successful; <see langword="false"/> if this result represents exception.</value>
        public bool IsSuccessful => exception is null;

        /// <summary>
        /// Extracts actual result.
        /// </summary>
        /// <exception cref="Exception">This result is not successful.</exception>
        public T Value
        {
            get
            {
                exception?.Throw();
                return value;
            }
        }

        /// <summary>
        /// If successful result is present, apply the provided mapping function hiding any exception
        /// caused by the converter.
        /// </summary>
        /// <param name="converter">A mapping function to be applied to the value, if present.</param>
        /// <typeparam name="U">The type of the result of the mapping function.</typeparam>
        /// <returns>The conversion result.</returns>
        public Result<U> Convert<U>(in ValueFunc<T, U> converter)
        {
            Result<U> result;
            if (exception is null)
                try
                {
                    result = converter.Invoke(value);
                }
                catch (Exception e)
                {
                    result = new Result<U>(e);
                }
            else
                result = new Result<U>(exception);
            return result;
        }

        /// <summary>
        /// If successful result is present, apply the provided mapping function hiding any exception
        /// caused by the converter.
        /// </summary>
        /// <param name="converter">A mapping function to be applied to the value, if present.</param>
        /// <typeparam name="U">The type of the result of the mapping function.</typeparam>
        /// <returns>The conversion result.</returns>
        public Result<U> Convert<U>(Converter<T, U> converter) => Convert(converter.AsValueFunc(true));

        /// <summary>
        /// Attempts to extract value from container if it is present.
        /// </summary>
        /// <param name="value">Extracted value.</param>
        /// <returns><see langword="true"/> if value is present; otherwise, <see langword="false"/>.</returns>
        public bool TryGet([NotNullWhen(true)]out T value)
        {
            value = this.value;
            return exception is null;
        }

        /// <summary>
        /// Returns the value if present; otherwise return default value.
        /// </summary>
        /// <param name="defaultValue">The value to be returned if this result is unsuccessful.</param>
        /// <returns>The value, if present, otherwise <paramref name="defaultValue"/>.</returns>
        public T Or(T defaultValue) => exception is null ? value : defaultValue;

        /// <summary>
        /// Returns the value if present; otherwise return default value.
        /// </summary>
        /// <returns>The value, if present, otherwise <c>default</c>.</returns>
        public T OrDefault() => value;

        /// <summary>
        /// Returns the value if present; otherwise invoke delegate.
        /// </summary>
        /// <param name="defaultFunc">A delegate to be invoked if value is not present.</param>
        /// <returns>The value, if present, otherwise returned from delegate.</returns>
        public T OrInvoke(in ValueFunc<T> defaultFunc) => exception is null ? value : defaultFunc.Invoke();

        /// <summary>
        /// Returns the value if present; otherwise invoke delegate.
        /// </summary>
        /// <param name="defaultFunc">A delegate to be invoked if value is not present.</param>
        /// <returns>The value, if present, otherwise returned from delegate.</returns>
        public T OrInvoke(Func<T> defaultFunc) => OrInvoke(new ValueFunc<T>(defaultFunc, true));

        /// <summary>
        /// Returns the value if present; otherwise invoke delegate.
        /// </summary>
        /// <param name="defaultFunc">A delegate to be invoked if value is not present.</param>
        /// <returns>The value, if present, otherwise returned from delegate.</returns>
        public T OrInvoke(in ValueFunc<Exception, T> defaultFunc) => exception is null ? value : defaultFunc.Invoke(exception.SourceException);

        /// <summary>
        /// Returns the value if present; otherwise invoke delegate.
        /// </summary>
        /// <param name="defaultFunc">A delegate to be invoked if value is not present.</param>
        /// <returns>The value, if present, otherwise returned from delegate.</returns>
        public T OrInvoke(Func<Exception, T> defaultFunc) => OrInvoke(new ValueFunc<Exception, T>(defaultFunc, true));

        /// <summary>
        /// Gets exception associated with this result.
        /// </summary>
        public Exception? Error => exception?.SourceException;

        /// <summary>
        /// Gets boxed representation of the result.
        /// </summary>
        /// <returns>The boxed representation of the result.</returns>
        public Result<object?> Box() => exception is null ? new Result<object?>(value) : new Result<object?>(exception);

        /// <summary>
        /// Extracts actual result.
        /// </summary>
        /// <param name="result">The result object.</param>
        [return: MaybeNull]
        public static explicit operator T(in Result<T> result) => result.Value;

        /// <summary>
        /// Converts the result into <see cref="Optional{T}"/>.
        /// </summary>
        /// <param name="result">The result to be converted.</param>
        public static implicit operator Optional<T>(in Result<T> result) => result.exception is null ? new Optional<T>(result.value) : Optional<T>.Empty;

        /// <summary>
        /// Converts value into the result.
        /// </summary>
        /// <param name="result">The result to be converted.</param>
        public static implicit operator Result<T>(T result) => new Result<T>(result);

        /// <summary>
        /// Indicates that both results are successful.
        /// </summary>
        /// <param name="left">The first result to check.</param>
        /// <param name="right">The second result to check.</param>
        /// <returns><see langword="true"/> if both results are successful; otherwise, <see langword="false"/>.</returns>
        public static bool operator &(in Result<T> left, in Result<T> right) => left.exception is null && right.exception is null;

        /// <summary>
        /// Indicates that the result is successful.
        /// </summary>
        /// <param name="result">The result to check.</param>
        /// <returns><see langword="true"/> if this result is successful; <see langword="false"/> if this result represents exception.</returns>
        public static bool operator true(in Result<T> result) => result.exception is null;

        /// <summary>
        /// Indicates that the result represents error.
        /// </summary>
        /// <param name="result">The result to check.</param>
        /// <returns><see langword="false"/> if this result is successful; <see langword="true"/> if this result represents exception.</returns>
        public static bool operator false(in Result<T> result) => !(result.exception is null);

        void ISerializable.GetObjectData(SerializationInfo info, StreamingContext context)
        {
            var exception = this.exception?.SourceException;
            info.AddValue(ExceptionSerData, exception, exception?.GetType() ?? typeof(Exception));
            info.AddValue(ValueSerData, value, typeof(T));
        }

        /// <summary>
        /// Returns textual representation of this object.
        /// </summary>
        /// <returns>The textual representation of this object.</returns>
        public override string ToString() => exception?.SourceException.ToString() ?? value?.ToString() ?? "<NULL>";
    }
}
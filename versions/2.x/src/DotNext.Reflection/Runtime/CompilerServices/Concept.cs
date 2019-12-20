using System;
using static System.Runtime.CompilerServices.RuntimeHelpers;
using static System.Runtime.ExceptionServices.ExceptionDispatchInfo;

namespace DotNext.Runtime.CompilerServices
{
    using Reflection;

    /// <summary>
    /// Provides a check of constraints defined by concept types.
    /// </summary>
    public static class Concept
    {
        /// <summary>
        /// Applies constraints described by concept type.
        /// </summary>
        /// <param name="conceptType">A static type describing concept.</param>
        /// <exception cref="ConstraintViolationException">One or more constraints defined by concept type are violated.</exception>
        /// <exception cref="ArgumentException"><paramref name="conceptType"/> is not marked with <see cref="ConceptAttribute"/>.</exception>
        public static void Assert(Type conceptType)
        {
            if (!conceptType.IsDefined<ConceptAttribute>())
                throw new ArgumentException(ExceptionMessages.ConceptTypeInvalidAttribution<ConceptAttribute>(conceptType), nameof(conceptType));
            try
            {
                //run class constructor for concept type and its parents
                while (!(conceptType is null))
                {
                    RunClassConstructor(conceptType.TypeHandle);
                    conceptType = conceptType.BaseType;
                }
            }
            catch (TypeInitializationException e) when (e.InnerException is ConstraintViolationException violation)
            {
                Capture(violation).Throw();
            }
        }

        /// <summary>
        /// Applies constraints described by concept type.
        /// </summary>
        /// <typeparam name="C">A type describing concept.</typeparam>
        /// <exception cref="ConstraintViolationException">One or more constraints defined by concept type are violated.</exception>
        /// <exception cref="ArgumentException"><typeparamref name="C"/> is not marked with <see cref="ConceptAttribute"/>.</exception>
        public static void Assert<C>() => Assert(typeof(C));

        /// <summary>
        /// Applies a chain of constraints described by multiple concept types.
        /// </summary>
        /// <param name="conceptType">A static type describing concept.</param>
        /// <param name="other">A set of static types describing concept.</param>
        /// <exception cref="ConstraintViolationException">Constraints defined by concept types are violated.</exception>
        /// <exception cref="ArgumentException">One or more concept types are not marked with <see cref="ConceptAttribute"/>.</exception>
        public static void Assert(Type conceptType, params Type[] other)
        {
            Assert(conceptType);
            Array.ForEach(other, Assert);
        }
    }
}
﻿using System;

namespace DotNext.Reflection
{
    /// <summary>
    /// Indicates that requested event doesn't exist.
    /// </summary>
    public sealed class MissingEventException : ConstraintViolationException
    {
        /// <summary>
        /// Initializes a new exception indicating that requested event doesn't exist.
        /// </summary>
        /// <param name="declaringType">The inspected type.</param>
        /// <param name="eventName">The name of the event.</param>
        /// <param name="handlerType">The type of the event handler.</param>
		public MissingEventException(Type declaringType, string eventName, Type handlerType)
            : base(declaringType, ExceptionMessages.MissingEvent(eventName, handlerType, declaringType))
        {
            HandlerType = handlerType;
            EventName = eventName;
        }

        internal static MissingEventException Create<T, E>(string eventName)
            where E : MulticastDelegate
            => new MissingEventException(typeof(T), eventName, typeof(E));

        /// <summary>
        /// Gets event handler type.
        /// </summary>
		public Type HandlerType { get; }

        /// <summary>
        /// Gets name of the missing event.
        /// </summary>
		public string EventName { get; }
    }
}

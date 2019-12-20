﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DotNext.Reflection
{
    /// <summary>
    /// Various extension methods for type reflection.
    /// </summary>
    public static class TypeExtensions
    {
        private const string IsUnmanagedAttributeName = "System.Runtime.CompilerServices.IsUnmanagedAttribute";

        /// <summary>
        /// Determines whether the type is read-only (immutable) value type.
        /// </summary>
        /// <param name="type">The type to inspect.</param>
        /// <returns><see langword="true"/>, if the specified type is immutable value type; otherwise, <see langword="false"/>.</returns>
        public static bool IsImmutable(this Type type)
        {
            if (type.IsPrimitive)
                return true;
            else if (type.IsValueType)
                foreach (var attribute in type.GetCustomAttributesData())
                    if (attribute.AttributeType == typeof(IsReadOnlyAttribute))
                        return true;
            return false;
        }

        /// <summary>
        /// Determines whether the type is unmanaged value type.
        /// </summary>
        /// <param name="type">The type to inspect.</param>
        /// <returns><see langword="true"/>, if the specified type is unmanaged value type; otherwise, <see langword="false"/>.</returns>
        public static bool IsUnmanaged(this Type type)
        {
            if (type.IsGenericType || type.IsGenericTypeDefinition || type.IsGenericParameter)
            {
                foreach (var attribute in type.GetCustomAttributesData())
                    if (attribute.AttributeType.FullName == IsUnmanagedAttributeName)
                        return true;
                return false;
            }
            else if (type.IsPrimitive || type.IsPointer || type.IsEnum)
                return true;
            else if (type.IsValueType)
            {
                //check all fields
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic))
                    if (!field.FieldType.IsUnmanaged())
                        return false;
                return true;
            }
            else
                return false;
        }

        /// <summary>
        /// Returns read-only collection of base types and, optionally, all implemented interfaces.
        /// </summary>
        /// <param name="type">The type to be discovered.</param>
        /// <param name="includeTopLevel"><see langword="true"/> to return <paramref name="type"/> as first element in the collection.</param>
        /// <param name="includeInterfaces"><see langword="true"/> to include implemented interfaces; <see langword="false"/> to return inheritance hierarchy only.</param>
        /// <returns>Read-only collection of base types and, optionally, all implemented interfaces.</returns>
        public static IEnumerable<Type> GetBaseTypes(this Type type, bool includeTopLevel = false, bool includeInterfaces = false)
        {
            for (var lookup = includeTopLevel ? type : type.BaseType; !(lookup is null); lookup = lookup.BaseType)
                yield return lookup;
            if (includeInterfaces)
                foreach (var iface in type.GetInterfaces())
                    yield return iface;
        }

        /// <summary>
        /// Returns method that overrides the specified method.
        /// </summary>
        /// <param name="type">The type that contains overridden method.</param>
        /// <param name="abstractMethod">The abstract method definition.</param>
        /// <returns>The method that overrides <paramref name="abstractMethod"/>.</returns>
        public static MethodInfo? Devirtualize(this Type type, MethodInfo abstractMethod)
        {
            if (abstractMethod.IsFinal || !abstractMethod.IsVirtual)
                return abstractMethod;
            if (type.IsInterface)
                goto exit;
            if (abstractMethod.DeclaringType.IsInterface && abstractMethod.DeclaringType.IsAssignableFrom(type))
            {
                //Interface maps for generic interfaces on arrays cannot be retrieved.
                if (type.IsArray && abstractMethod.DeclaringType.IsGenericType)
                    goto exit;
                var interfaceMap = type.GetInterfaceMap(abstractMethod.DeclaringType);
                for (var i = 0L; i < interfaceMap.InterfaceMethods.LongLength; i++)
                    if (interfaceMap.InterfaceMethods[i] == abstractMethod)
                        return interfaceMap.TargetMethods[i];
                goto exit;
            }
            //handle virtual method
            foreach (var lookup in GetBaseTypes(type, includeTopLevel: true))
                foreach (var candidate in lookup.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    if (candidate.GetBaseDefinition() == abstractMethod)
                        return candidate;
            exit:
            return null;
        }

        internal static Type? FindGenericInstance(this Type type, Type genericDefinition)
        {
            bool IsGenericInstanceOf(Type candidate)
                => candidate.IsGenericType && !candidate.IsGenericTypeDefinition && candidate.GetGenericTypeDefinition() == genericDefinition;

            if (genericDefinition.IsInterface)
            {
                foreach (var iface in type.GetInterfaces())
                    if (IsGenericInstanceOf(iface))
                        return iface;
            }
            else
                while (!(type is null))
                    if (IsGenericInstanceOf(type))
                        return type;
                    else
                        type = type.BaseType;
            return null;
        }

        /// <summary>
        /// Determines whether the type is an instance of the specified generic type.
        /// </summary>
        /// <param name="type">The type to check.</param>
        /// <param name="genericDefinition">Generic type definition.</param>
        /// <returns><see langword="true"/>, if the type is an instance of the specified generic type; otherwise, <see langword="false"/>.</returns>
        /// <example>
        /// <code>
        /// typeof(byte[]).IsGenericInstanceOf(typeof(IEnumerable&lt;&gt;));    //returns true
        /// typeof(List&lt;int&gt;).IsGenericInstanceOf(typeof(List&lt;int&gt;));   //returns true
        /// </code>
        /// </example>
		public static bool IsGenericInstanceOf(this Type type, Type genericDefinition)
            => !(FindGenericInstance(type, genericDefinition) is null);

        /// <summary>
        /// Returns actual generic arguments passed into generic type definition implemented by the input type.
        /// </summary>
        /// <param name="type"></param>
        /// <param name="genericDefinition"></param>
        /// <returns></returns>
        /// <example>
        /// <code>
        /// var elementTypes = typeof(byte[]).IsGenericInstanceOf(typeof(IEnumerable&lt;&gt;));
        /// elementTypes[0] == typeof(byte); //true
        /// </code>
        /// </example>
		public static Type[] GetGenericArguments(this Type type, Type genericDefinition)
            => FindGenericInstance(type, genericDefinition)?.GetGenericArguments() ?? Array.Empty<Type>();

        /// <summary>
        /// Indicates that object of one type can be implicitly converted into another without boxing.
        /// </summary>
        /// <param name="to">Type of conversion result.</param>
        /// <param name="from">The type check.</param>
        /// <returns><see langword="true"/> if <paramref name="from"/> is implicitly convertible into <paramref name="to"/> without boxing.</returns>
        /// <seealso cref="Type.IsAssignableFrom(Type)"/>
        /// <example>
        /// <code>
        /// typeof(object).IsAssignableFrom(typeof(int)); //true
        /// typeof(object).IsAssignableFromWithoutBoxing(typeof(int)); //false
        /// typeof(object).IsAssignableFrom(typeof(string));    //true
        /// typeof(object).IsAssignableFromWithoutBoxing(typeof(string));//true
        /// </code>
        /// </example>
        public static bool IsAssignableFromWithoutBoxing(this Type to, Type from)
            => to == from || !from.IsValueType && to.IsAssignableFrom(from);

        /// <summary>
        /// Casts an object to the class, value type or interface.
        /// </summary>
        /// <param name="type">The type cast result.</param>
        /// <param name="obj">The object to be cast.</param>
        /// <returns>The object after casting, or <see langword="null"/> if <paramref name="obj"/> is <see langword="null"/>.</returns>
        /// <exception cref="InvalidCastException">
        /// If the object is not <see langword="null"/> and is not assignable to the <paramref name="type"/>; 
        /// or if object is <see langword="null"/> and <paramref name="type"/> is value type.
        /// </exception>
        [return: NotNullIfNotNull("obj")]
        public static object? Cast(this Type type, object? obj)
        {
            if (obj is null)
                return type.IsValueType ? throw new InvalidCastException(ExceptionMessages.CastNullToValueType) : default(object);
            if (type.IsInstanceOfType(obj))
                return obj;
            throw new InvalidCastException();
        }
    }
}
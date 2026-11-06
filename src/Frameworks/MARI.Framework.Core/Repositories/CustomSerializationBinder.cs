// Copyright (c) Kaonomi.
// The MARI licenses this file to you under the MIT license.
// Produce by kaonomi & community (cf. docs/Teams.md)

namespace MARI.Framework.Core.Repositories
{
    using Newtonsoft.Json.Serialization;

    using System;
    using System.Collections.Concurrent;
    using System.Collections.Generic;

    /// <summary>
    /// Allow-list <see cref="ISerializationBinder"/> used whenever MARI deserializes JSON that may
    /// carry "$type" metadata. Only types declared in a MARI or Orleans assembly (plus the
    /// assemblies explicitly registered through <see cref="AllowAssembly"/>) are materialised;
    /// anything else resolves to <c>null</c>, which Newtonsoft turns into a controlled
    /// <see cref="Newtonsoft.Json.JsonSerializationException"/> instead of a gadget instantiation.
    /// Assembly version, culture and public key token are deliberately ignored so a payload stored
    /// with one build still deserializes after an assembly version bump.
    /// </summary>
    internal sealed class CustomSerializationBinder : ISerializationBinder
    {
        #region Fields

        private const string MARI_ASSEMBLY_PREFIX = "MARI.";
        private const string ORLEANS_ASSEMBLY_PREFIX = "Orleans.";

        private static readonly ConcurrentDictionary<string, Type?> s_resolvedTypes = new(StringComparer.Ordinal);
        private static readonly HashSet<string> s_allowedAssemblies = new(StringComparer.OrdinalIgnoreCase);

        #endregion

        #region Methods

        /// <summary>
        /// Registers an extra assembly (simple name, e.g. <c>"Hermes.Bridge"</c>) whose types are
        /// allowed in payloads. Call it once at startup, before the first deserialization.
        /// </summary>
        public static void AllowAssembly(string assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                throw new ArgumentException("Assembly name is required.", nameof(assemblyName));

            lock (s_allowedAssemblies)
            {
                if (s_allowedAssemblies.Add(assemblyName))
                {
                    // Drop the negative cache: a type rejected before this registration must be
                    // resolvable now that its assembly joined the allow-list.
                    s_resolvedTypes.Clear();
                }
            }
        }

        /// <inheritdoc />
        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            // Simple assembly name only: a fully qualified name would pin the payload to the
            // current version and break as soon as MARI is rebuilt with a new AssemblyVersion.
            assemblyName = serializedType.Assembly.GetName().Name;
            typeName = serializedType.FullName;
        }

        /// <inheritdoc />
        public Type BindToType(string? assemblyName, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null!;

            var simpleAssemblyName = GetSimpleAssemblyName(assemblyName);
            var cacheKey = simpleAssemblyName + "|" + typeName;

            if (s_resolvedTypes.TryGetValue(cacheKey, out var cached))
                return cached!;

            var resolved = Resolve(simpleAssemblyName, typeName);

            // Defence in depth: the allow-list is checked on the resolved type too, so a payload
            // without assembly name cannot smuggle a type from an unexpected assembly.
            var allowed = resolved is not null && IsAllowedAssembly(resolved.Assembly.GetName().Name);

            s_resolvedTypes.TryAdd(cacheKey, allowed ? resolved : null);

            // null! is intentional: the interface declares a non-nullable return, and Newtonsoft
            // turns a null result into a controlled JsonSerializationException.
            return allowed ? resolved! : null!;
        }

        #endregion

        #region Tools

        private static Type? Resolve(string? simpleAssemblyName, string typeName)
        {
            if (simpleAssemblyName is not null && IsAllowedAssembly(simpleAssemblyName))
            {
                var qualified = Type.GetType($"{typeName}, {simpleAssemblyName}", throwOnError: false);
                if (qualified is not null)
                    return qualified;
            }

            // Fallback: the assembly may be loaded under a different simple name, or the payload
            // may omit it entirely. Only allowed assemblies are scanned.
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsAllowedAssembly(assembly.GetName().Name))
                    continue;

                var type = assembly.GetType(typeName, throwOnError: false);
                if (type is not null)
                    return type;
            }

            return null;
        }

        private static bool IsAllowedAssembly(string? assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                return false;

            if (assemblyName.StartsWith(MARI_ASSEMBLY_PREFIX, StringComparison.Ordinal)
                || assemblyName.StartsWith(ORLEANS_ASSEMBLY_PREFIX, StringComparison.Ordinal))
                return true;

            lock (s_allowedAssemblies)
                return s_allowedAssemblies.Contains(assemblyName);
        }

        /// <summary>
        /// Reduces an assembly identity (<c>"MARI.Framework.Core, Version=1.0.0.0, Culture=neutral,
        /// PublicKeyToken=null"</c>) to its simple name, ignoring version and key material.
        /// </summary>
        private static string? GetSimpleAssemblyName(string? assemblyName)
        {
            if (string.IsNullOrWhiteSpace(assemblyName))
                return null;

            var comma = assemblyName.IndexOf(',');
            var simpleName = comma < 0 ? assemblyName : assemblyName.Substring(0, comma);

            simpleName = simpleName.Trim();

            return simpleName.Length == 0 ? null : simpleName;
        }

        #endregion
    }
}

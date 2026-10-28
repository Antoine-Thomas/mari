using System;
using Newtonsoft.Json.Serialization;

namespace MARI.Framework.Core.Repositories
{
    internal class CustomSerializationBinder : ISerializationBinder
    {
        // Allow-list of prefixes for safe types (MARI and Orleans)
        private static readonly string[] AllowedPrefixes =
        {
            "MARI.",
            "Orleans."
        };

        public void BindToName(Type serializedType, out string assemblyName, out string typeName)
        {
            assemblyName = serializedType.Assembly.FullName;
            typeName = serializedType.FullName;
        }

        public Type BindToType(string assemblyName, string typeName)
        {
            // Reject any type outside the allow-list: return null so
            // Newtonsoft throws a controlled JsonSerializationException.
            foreach (var prefix in AllowedPrefixes)
            {
                if (typeName.StartsWith(prefix))
                {
                    try
                    {
                        return Type.GetType($"{typeName}, {assemblyName}");
                    }
                    catch
                    {
                        return null;
                    }
                }
            }
            return null;
        }
    }
}
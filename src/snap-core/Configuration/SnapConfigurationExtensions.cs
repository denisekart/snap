using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LibGit2Sharp;
using Microsoft.Data.SqlClient;

namespace Snap.Core
{
    public static class SnapConfigurationExtensions
    {
        private static string GetValueOr(Dictionary<string, string> dictionary, string key, string defaultValue = null)
        {
            if (dictionary == null)
                return defaultValue;
            if (!dictionary.ContainsKey(key))
                return defaultValue;
            return dictionary[key];
        }
        public static string GetGitRepositoryRootPropertyOrDefault(this SnapConfiguration config, string defaultValue = null)
        {
            return GetValueOr(config.Properties, "GitRepositoryRoot", defaultValue);
        }

        public static bool GetIsRunningInDockerContainer(this SnapConfiguration.Target target)
        {
            return target.IsRunningInDocker;
        }

        public static string GetConnectionStringProperty(this SnapConfiguration.Target target)
        {
            return GetValueOr(target.Properties, "ConnectionString") ??
                   throw new System.Exception("Missing property 'ConnectionString' in target " + target.Type);
        }

        public static string GetHostProperty(this SnapConfiguration.Target target)
        {
            return GetValueOr(target.Properties, "Host") ??
                   throw new System.Exception("Missing property 'Host' in target " + target.Type);
        }

        public static string GetContainerIdProperty(this SnapConfiguration.Target target)
        {
            return GetValueOr(target.Properties, "ContainerId") ??
                   throw new System.Exception("Missing property 'ContainerId' in target " + target.Type);
        }

        public static string GenerateTargetUniqueName(this SnapConfiguration config, SnapConfiguration.Target target)
        {
            StringBuilder sb = new StringBuilder();
            var nameParts = target.NameParts;
            if (target.NameParts == null || target.NameParts.Count == 00)
            {
                nameParts = new List<string>
                {
                    config.Name,
                    target.Name,
                    target.Type,
                    "ConnectionString",
                    "Host",
                    "ContainerId",
                    "GitRepositoryRoot"
                };
            }

            return string.Join('_', nameParts.Select(x =>
            {
                if (x == null)
                    return null;

                if (_partGenerator.ContainsKey(x)
                    && (GetValueOr(config.Properties, x) ?? GetValueOr(target.Properties, x)) is string value)
                {
                    return string.Join('-', _partGenerator[x](value) ?? new string[] { });
                }

                return x;
            }).OfType<string>())
                .Replace('/', '_')
                .Replace('\\', '_')
                .Replace('.', '_'); ;
        }

        private static Dictionary<string, Func<string, string[]>> _partGenerator =
            new Dictionary<string, Func<string, string[]>>
            {
                {"ConnectionString", value =>
                {
                    if(string.IsNullOrWhiteSpace(value))
                        return null;
                    var connectionStringBuilder = new SqlConnectionStringBuilder(value);
                    return new []{connectionStringBuilder.DataSource, connectionStringBuilder.InitialCatalog};
                }},
                {"Host",  value =>
                {
                    if(string.IsNullOrWhiteSpace(value))
                        return null;
                    return new []{value};
                }},
                {"ContainerId",  value =>
                {
                    if(string.IsNullOrWhiteSpace(value))
                        return null;

                    return new []{value};
                }},
                {"GitRepositoryRoot",  value =>
                {
                    if(string.IsNullOrWhiteSpace(value))
                        return null;

                    var repo = new Repository(value);

                    return new []{repo.Head.FriendlyName};
                }},

            };
    }
}
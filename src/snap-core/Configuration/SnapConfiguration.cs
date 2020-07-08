using System.Collections.Generic;

namespace Snap.Core
{
    public class SnapConfiguration
    {
        public SnapConfiguration(string configurationDirectory, string configurationFile)
        {
            ConfigurationDirectory = configurationDirectory;
            ConfigurationFile = configurationFile;
        }

        /// <summary>
        /// Global configuration name to make this configuration unique
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The directory from which this configuration is being read
        /// </summary>
        public string ConfigurationDirectory { get; }

        /// <summary>
        /// The name of the file of the configuration. Usualy snap.json
        /// </summary>
        public string ConfigurationFile { get; }

        /// <summary>
        /// Targets
        /// </summary>
        public List<Target> Targets { get; set; }

        /// <summary>
        /// Global properties
        /// </summary>
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Target spec
        /// </summary>
        public class Target
        {
            /// <summary>
            /// Targer name to make this target unique
            /// </summary>
            public string Name { get; set; }

            /// <summary>
            /// Target type
            /// </summary>
            public string Type { get; set; }

            /// <summary>
            /// Specifies that the target is virtual
            /// </summary>
            public bool IsRunningInDocker { get; set; }

            /// <summary>
            /// Target specific properties
            /// </summary>
            public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

            /// <summary>
            /// The pack target
            /// </summary>
            public CommonTask Pack { get; set; }

            /// <summary>
            /// The unpack/restore target
            /// </summary>
            public CommonTask Unpack { get; set; }

            /// <summary>
            /// The clean target
            /// </summary>
            public CommonTask Clean { get; set; }

            /// <summary>
            /// The parts to use when constructing a unique name
            /// </summary>
            public List<string> NameParts { get; set; }
        }

        /// <summary>
        /// Common properties for all targets
        /// </summary>
        public class CommonTask
        {
            public bool Enable { get; set; }
        }
    }
}
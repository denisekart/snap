using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using EnvironmentBuilder;
using EnvironmentBuilder.Abstractions;
using EnvironmentBuilder.Extensions;
using LibGit2Sharp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using Nest;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;
using Snap.Core.Runners;

namespace Snap.Core
{
    public class SnapRunner
    {
        private readonly IEnvironmentBuilder _environment;
        private Targets _targets;
        private SnapConfiguration _configuration;
        private readonly TargetRegistry _targetRegistry;

        public SnapRunner(IEnvironmentBuilder environment, SnapConfiguration configuration, TargetRegistry targetRegistry)
        {
            _environment = environment;
            _configuration = configuration;
            _targetRegistry = targetRegistry;

            var pack = _environment.WithDescription("Packs a snapshot of the environment")
                .Arg("pack").Arg("p").Default(false).Bundle().Build<bool>();

            var unpack = _environment.WithDescription("Unpacks/restores the snapshot from the current environment")
                .Arg("unpack").Arg("u").Arg("restore").Default(false).Bundle().Build<bool>();

            var clean = _environment.WithDescription("Cleans the current environment and makes it ready for an unpack. This command may delete the data you're working on!'")
                .Arg("clean").Arg("c").Default(false).Bundle().Build<bool>();

            if (_environment.Arg("help").Build<bool>())
            {
                _targets |= Targets.Help;
                return;
            }

            if (!(clean || pack || unpack))
            {
                LogAndThrow($"No command to run. \r\n{_environment.GetHelp()}");
            }

            if (pack && (unpack || clean))
            {
                LogAndThrow("Cannot use the 'pack' command with 'clean' or 'unpack' commands.");
            }

            if (pack)
                _targets |= Targets.Pack;

            if (unpack)
                _targets |= Targets.Unpack;

            if (clean)
                _targets |= Targets.Clean;
        }

        private static SnapConfiguration CreateConfiguration(string configurationLocation)
        {
            var absoluteRoute = default(string);
            if (string.IsNullOrWhiteSpace(configurationLocation) && File.Exists(Path.Join(Directory.GetCurrentDirectory(), "snap.json")))
            {
                absoluteRoute = Path.GetFullPath(Path.Join(Directory.GetCurrentDirectory(), "snap.json"));
            }
            else if (File.Exists(configurationLocation))
            {
                absoluteRoute = Path.GetFullPath(configurationLocation);
            }
            else if (Directory.Exists(configurationLocation) && File.Exists(Path.Join(configurationLocation, "snap.json")))
            {
                absoluteRoute = Path.GetFullPath(Path.Join(configurationLocation, "snap.json"));
            }
            else
            {
                throw new Exception($"Configuration does not exist ('{configurationLocation}')");
            }

            ValidateConfiguration(absoluteRoute);

            var settings = new SnapConfiguration(Path.GetDirectoryName(absoluteRoute), Path.GetFileName(absoluteRoute));
            new ConfigurationBuilder()
                .AddJsonFile(absoluteRoute)
                .Build().Bind(settings);

            return settings;
        }

        private static void ValidateConfiguration(string absoluteRoute)
        {
            using (var sr = new StreamReader(Assembly.GetExecutingAssembly()
                            .GetManifestResourceStream("Snap.Core.Schemas.configuration-schema.json")))
            using (var tr = new JsonTextReader(sr))
            {
                var configutationSchema = JSchema.Load(tr);
                var configuration = JObject.Parse(File.ReadAllText(absoluteRoute));

                if (!configuration.IsValid(configutationSchema, out IList<string> errors))
                {
                    throw new Exception($"Configuration at '{absoluteRoute}' is not valid according to the schema. \r\n{string.Join("\r\n", errors)}");
                }
            }
        }

        /// <summary>
        /// This is the default entry point into the runner.
        /// </summary>
        /// <returns></returns>
        public static SnapRunner Create()
        {
            var manager = EnvironmentManager.Create(cfg =>
                cfg
                    .WithEnvironmentVariablePrefix("SNAP_"));
            var configuration = manager
                .WithDescription(
                    "Sets the configuration file/folder to use. If a folder is specified then the file 'snap.json' should be present")
                .Arg("configuration").Arg("config").Arg("c").Default("./snap.json").Bundle().Build();

            var snapConfiguration = CreateConfiguration(configuration);
            var serviceCollection = new ServiceCollection();

            Configure(serviceCollection, snapConfiguration, manager);
            var services = serviceCollection.BuildServiceProvider();

            return services.GetRequiredService<SnapRunner>();
        }

        private static void Configure(IServiceCollection serviceCollection, SnapConfiguration configuration, IEnvironmentBuilder manager)
        {
            serviceCollection.AddSingleton(configuration);
            serviceCollection.AddSingleton(manager);

            ConfigureRunners(serviceCollection, configuration);

            serviceCollection.AddSingleton<TargetRegistry>(resolver =>
            {
                var register = new TargetRegistry();

                foreach (var targetRunner in resolver.GetServices<ITargetRunner>())
                {
                    register.TryAddRunner(targetRunner.Type, targetRunner);
                }

                return register;
            });

            serviceCollection.AddSingleton<SnapRunner>();
        }

        private static void ConfigureRunners(IServiceCollection serviceCollection, SnapConfiguration configuration)
        {
            var path = "C:\\Sandbox";
            DotnetToolRunner.EnsureTool("dotnet-cleanup", path);

            List<string> existingRunners = new List<string>();
            void AddRunner<TRunner>(string type) where TRunner : class, ITargetRunner
            {
                if (!configuration.Targets.Any(x => x.Type.Equals(type)))
                    return;

                serviceCollection.AddSingleton<ITargetRunner, TRunner>();
                if (!existingRunners.Exists(p => p.Equals(type)))
                    existingRunners.Add(type);
            }

            // These should be moved to their own assemblies and packages in the future
            AddRunner<MssqlTargetRunner>("mssql");
            AddRunner<ElasticSearchRunner>("elasticsearch");

            configuration.Targets.ForEach(target =>
            {
                if (!existingRunners.Exists(t => t.Equals(target.Type)))
                {
                    // TODO: Install/restore and dynamically bind the tools to this instance of the runner
                }
            });
        }

        public void Run()
        {
            if (_targets.HasFlag(Targets.Help))
            {
                _environment.LogInformation(_environment.GetHelp());
                return;
            }

            if (_targets.HasFlag(Targets.Clean))
            {
                RunTask(r => r.Clean, r => r.Clean?.Enable ?? false);
            }

            if (_targets.HasFlag(Targets.Unpack))
            {
                RunTask(r => r.Restore, r => r.Restore?.Enable ?? false);
            }

            if (_targets.HasFlag(Targets.Pack))
            {
                RunTask(r => r.Pack, r => r.Pack?.Enable ?? false);
            }
        }

        private void RunTask(
            Func<ITargetRunner, Action<SnapConfiguration.Target>> taskToRun,
            Func<SnapConfiguration.Target, bool> filter)
        {
            foreach (var target in _configuration.Targets.Where(filter))
            {
                if (_targetRegistry.TryGetRunner(target.Type, out var runner))
                {
                    taskToRun(runner)(target);
                }
                else
                {
                    LogAndThrow($"Could not find target runner for type '{target.Type}'");
                }
            }
        }

        private void LogAndThrow(string message)
        {
            _environment.LogFatal(message);
            throw new SnapException(message, true);
        }
    }

    public class DotnetToolRunner
    {
        private static string ExecDotnet(string arguments)
        {
            Process process = new Process();
            process.StartInfo.EnvironmentVariables.Add("DOTNET_NOLOGO", "yes");
            process.StartInfo.FileName = "dotnet.exe";
            process.StartInfo.Arguments = arguments;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string err = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new Exception(output + "\r\n" + err);

            return output;
        }
        public static (string id, string version)[] List(string path)
        {
            var result = ExecDotnet($"tool list --tool-path \"{path}\"");

            var lines = result.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(2).ToList();

            return lines.Select(x=>
            {
                var items = x.Split("  ", StringSplitOptions.RemoveEmptyEntries);
                return (items[0], items[1]);
            }).ToArray();
        }

        public static void Install(string tool, string path)
        {
            ExecDotnet($"tool install {tool} --tool-path \"{path}\"");
        }

        public static void Restore(string tool, string path)
        {
            ExecDotnet($"tool restore {tool} --tool-path \"{path}\"");
        }

        public static void EnsureTool(string tool, string path)
        {
            if (!List(path).Any(x => x.id.Equals(tool)))
            {
                Install(tool, path);
            }
        }
    }
}
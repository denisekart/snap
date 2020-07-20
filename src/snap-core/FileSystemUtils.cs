using Docker.DotNet;
using Docker.DotNet.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers;
using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Snap.Core
{
    public static class FileSystemUtils
    {
        public static bool Remove(string from)
        {
            if (File.Exists(from))
            {
                File.Delete(from);
                return true;
            }

            return false;
        }

        public enum FileSystemType
        {
            Local,
            Docker
        }

        public static bool Move(string from, string to)
        {
            if (File.Exists(from))
            {
                File.Move(from, to, true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Moves files between logical an virtual memory locations (i.e. the filesystem and docker)
        /// </summary>
        /// <param name="from"></param>
        /// <param name="fromType"></param>
        /// <param name="to"></param>
        /// <param name="toType"></param>
        /// <param name="containerId">The sha id, or an ending of a container name. Must be unique in regards to all running containers</param>
        /// <returns></returns>
        public static bool MoveVirtual(string from, FileSystemType fromType, string to, FileSystemType toType,
            string containerId, bool extractTar = true)
        {
            if (fromType == FileSystemType.Local && toType == FileSystemType.Local)
            {
                return Move(from, to);
            }
            if (fromType == FileSystemType.Docker && toType == FileSystemType.Docker)
            {
                throw new NotImplementedException("Moving files between docker containers is not supported (TODO)");
            }

            var client = new DockerClientConfiguration(new Uri("npipe://./pipe/docker_engine")).CreateClient();
            var container = client.Containers.ListContainersAsync(
                    new ContainersListParameters())
                .GetAwaiter().GetResult()
                .Single(x => x.ID.Contains(containerId) || x.Names.Any(n => n.ToLower().EndsWith(containerId.ToLower())));

            if (fromType == FileSystemType.Docker)
            {
                client.Containers
                    .StopContainerAsync(container.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 10 })
                    .GetAwaiter().GetResult();
                var response = client.Containers
                    .GetArchiveFromContainerAsync(container.ID, new GetArchiveFromContainerParameters { Path = from },
                        false).GetAwaiter().GetResult();
                if (File.Exists(to))
                    File.Delete(to);

                using (var fileStream = File.Create(to + ".tar"))
                {
                    response.Stream.CopyTo(fileStream);
                }

                client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters()).GetAwaiter()
                    .GetResult();

                if (extractTar)
                {

                    using (var archive = ArchiveFactory.Open(to + ".tar"))
                    {
                        var entry = archive.Entries.Single(x =>
                            x.Key.Equals(Path.GetFileName(to), StringComparison.OrdinalIgnoreCase));
                        entry.WriteToDirectory(Path.GetDirectoryName(to),
                            new SharpCompress.Common.ExtractionOptions { Overwrite = true });
                    }

                    File.Delete(to + ".tar");
                }
            }
            else
            {
                var stagingArchivePath = from + ".tar";
                using (Stream stream = File.Create(stagingArchivePath))
                using (var writer = WriterFactory.Open(stream, ArchiveType.Tar, CompressionType.None))
                {
                    writer.Write(Path.GetFileName(from), from);
                }

                client.Containers.StopContainerAsync(container.ID, new ContainerStopParameters { WaitBeforeKillSeconds = 10 }).GetAwaiter().GetResult();

                using var archiveStream = File.OpenRead(stagingArchivePath);
                client.Containers
                    .ExtractArchiveToContainerAsync(container.ID, new ContainerPathStatParameters { AllowOverwriteDirWithFile = true, Path = to },
                        archiveStream).GetAwaiter().GetResult();
                var started = client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters()).GetAwaiter().GetResult();

            }

            return true;
        }
        /// <summary>
        /// Generates a "snap" specific path for the current user. If file name is specified the path with the filename is returned. Otherwise the directory name is returned instead.
        /// </summary>
        /// <param name="fileName">The file name. Defaults to null</param>
        /// <returns></returns>
        public static string GeneratePathForCurrentUser(string fileName = null, string extraPathSegments = null)
        {
            var rootPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "snap");
            rootPath = extraPathSegments == null ? rootPath : Path.Join(rootPath, extraPathSegments);

            if (!Directory.Exists(rootPath))
            {
                Directory.CreateDirectory(rootPath);
            }

            var di = new DirectoryInfo(rootPath);
            di.Attributes = FileAttributes.Normal;

            var ds = new DirectorySecurity(rootPath, AccessControlSections.Access);

            // Using this instead of the "Everyone" string means we work on non-English systems.
            SecurityIdentifier everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            ds.AddAccessRule(new FileSystemAccessRule(everyone, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

            return rootPath + (!string.IsNullOrWhiteSpace(fileName) ? $"\\{fileName}" : string.Empty);
        }

    }
}
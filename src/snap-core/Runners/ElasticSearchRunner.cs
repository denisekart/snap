using Nest;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Snap.Core.Runners
{
    [CoreRunner]
    public class ElasticSearchRunner : ITargetRunner
    {
        private readonly SnapConfiguration _configuration;

        public ElasticSearchRunner(SnapConfiguration configuration)
        {
            _configuration = configuration;
        }
        public string Type => "elasticsearch";

        public void Pack(SnapConfiguration.Target target)
        {
            var repositoryNameString = _configuration.GenerateTargetUniqueName(target);
            var repositoryName = new Name(repositoryNameString);
            var snapshotName = new Name(target.Name ?? "snapshot");

            var elastic = new ElasticClient(new Uri(target.GetHostProperty()));

            if (!elastic.Snapshot.VerifyRepository(repositoryName).IsValid)
            {
                Func<CreateRepositoryResponse> createRepo = () => elastic.Snapshot.CreateRepository(repositoryName, descriptor =>
                        descriptor.FileSystem(fs => fs.Settings(target.GetTargetDirectoryProperty())));

                var result = createRepo();

                Func<bool> missingRepo = () =>
                    Regex.IsMatch(result?.ServerError?.Error?.CausedBy?.Reason ?? "",
                        "\\[.*\\] location \\[.*\\] doesn't match any of the locations specified by path\\.repo because this setting is empty");

                if (missingRepo())
                {
                    AddRepoPathToElasticConfiguration(target);
                    result = createRepo();
                }

                if (!result.Acknowledged)
                {
                    throw new SnapException(
                        $"Could not create elasticsearch repository with name '{repositoryNameString}' at location '{target.GetTargetDirectoryProperty()}'");
                }
            }

            elastic.Snapshot.Delete(repositoryName, snapshotName);
            elastic.Snapshot.CleanupRepository(repositoryName);

            var snapshotResult =
                elastic.Snapshot.Snapshot(repositoryName, snapshotName, snapshot => snapshot.WaitForCompletion());
            if (!snapshotResult.Accepted)
            {
                throw new SnapException("Snapshot creation was not accepted");
            }

            FileSystemUtils.MoveVirtual(
                target.GetTargetDirectoryProperty()+"\\".Replace("\\", "/."), 
                FileSystemUtils.FileSystemType.Docker,
                FileSystemUtils.GeneratePathForCurrentUser(repositoryNameString), 
                FileSystemUtils.FileSystemType.Local,
                target.GetContainerIdProperty(),
                false);
        }

        private void AddRepoPathToElasticConfiguration(SnapConfiguration.Target target)
        {
            if (target.GetIsRunningInDockerContainer())
            {
                var esFile = Path.GetFileName(target.GetElasticSearchConfigurationFileProperty());

                var stagingPath = FileSystemUtils.GeneratePathForCurrentUser(esFile, $"staging\\{target.Name ?? target.Type}");

                FileSystemUtils.MoveVirtual(target.GetElasticSearchConfigurationFileProperty(),
                    FileSystemUtils.FileSystemType.Docker, stagingPath, FileSystemUtils.FileSystemType.Local,
                    target.GetContainerIdProperty());

                File.AppendAllText(stagingPath, Environment.NewLine +
                                                  $"path.repo: ['{target.GetTargetDirectoryProperty()}']");

                FileSystemUtils.MoveVirtual(
                    stagingPath,
                    FileSystemUtils.FileSystemType.Local,
                    Path.GetDirectoryName(target.GetElasticSearchConfigurationFileProperty()),
                    FileSystemUtils.FileSystemType.Docker, target.GetContainerIdProperty());
            }
            else
            {
                throw new NotImplementedException("This functionality is not implemented yet. Ensure the 'repo.path' is configured in the elasticsearch.yml file.");
            }
        }

        public void Restore(SnapConfiguration.Target target)
        {
            throw new NotImplementedException();
        }

        public void Clean(SnapConfiguration.Target target)
        {
            throw new NotImplementedException();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using EnvironmentBuilder;
using EnvironmentBuilder.Abstractions;
using EnvironmentBuilder.Extensions;
using LibGit2Sharp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Snap.Core
{
    [Flags]
    public enum Targets
    {
        Pack = 1,
        Unpack = 2,
        Clean = 4,
        Help = 8
    }
    public class SnapRunner
    {
        private readonly IEnvironmentBuilder _environment;
        private Targets _targets;
        private SnapConfiguration _configuration;

        private SnapRunner(IEnvironmentBuilder environment)
        {
            _environment = environment;

            var pack = _environment.WithDescription("Packs a snapshot of the environment")
                .Arg("pack").Arg("p").Default(false).Bundle().Build<bool>();

            var unpack = _environment.WithDescription("Unpacks/restores the snapshot from the current environment")
                .Arg("unpack").Arg("u").Arg("restore").Default(false).Bundle().Build<bool>();

            var clean = _environment.WithDescription("Cleans the current environment and makes it ready for an unpack. This command may delete the data you're working on!'")
                .Arg("clean").Arg("c").Default(false).Bundle().Build<bool>();

            var configuration = _environment
                .WithDescription(
                    "Sets the configuration file/folder to use. If a folder is specified then the file 'snap.json' should be present")
                .Arg("configuration").Arg("config").Arg("c").Default("./snap.json").Bundle().Build();

            if (_environment.Arg("help").Build<bool>())
            {
                _targets |= Targets.Help;
                return;
            }

            _configuration = CreateConfiguration(configuration);

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
        private SnapConfiguration CreateConfiguration(string configurationLocation)
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
                LogAndThrow($"Configuration does not exist ('{configurationLocation}')");
            }

            var settings = new SnapConfiguration();
            new ConfigurationBuilder()
                .AddJsonFile(absoluteRoute)
                .Build().Bind(settings);
            settings.ConfigurationDirectory = Path.GetDirectoryName(absoluteRoute);
            settings.ConfigurationFile = Path.GetFileName(absoluteRoute);
            return settings;
        }
        public static SnapRunner Create()
        {
            return new SnapRunner(EnvironmentManager.Create(cfg =>
                cfg
                    .WithEnvironmentVariablePrefix("SNAP_")
                    .WithConsoleLogger()
                    .WithJsonFile("snap.json")
                ));
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
                RunClean();
            }

            if (_targets.HasFlag(Targets.Unpack))
            {
                RunUnpack();
            }

            if (_targets.HasFlag(Targets.Pack))
            {
                RunPack();
            }
        }
        private void RunPack()
        {
            foreach (var target in _configuration.Targets.Where(t => t.Pack != null && t.Pack.Enable))
            {
                switch (target.Type)
                {
                    case "mssql":
                        RunMssqlPack(target); break;
                    default: LogAndThrow($"Unknown target type {target}"); break;
                }
            }
        }

        private string GetMssqlTargetUniqueName(SnapConfiguration.Target target)
        {
            StringBuilder sb = new StringBuilder();
            var connectionStringBuilder = new SqlConnectionStringBuilder(target.Properties["ConnectionString"]);
            sb.AppendJoin("_", target.Type, connectionStringBuilder.DataSource, connectionStringBuilder.InitialCatalog);

            if (_configuration.Properties.ContainsKey("GitRepositoryRoot"))
            {
                var repo = new Repository(_configuration.Properties["GitRepositoryRoot"]);
                sb.Append($"_{repo.Head.FriendlyName}");
            }

            return sb.ToString();
        }
        private void RunMssqlPack(SnapConfiguration.Target target)
        {
            var connectionStringBuilder = new SqlConnectionStringBuilder(target.Properties["ConnectionString"]);

            Server srv = new Server(new ServerConnection(new SqlConnection(connectionStringBuilder.ConnectionString)));

            Database db = srv.Databases[connectionStringBuilder.InitialCatalog];

            // Store the current recovery model in a variable.   
            int recoveryModel = (int)db.DatabaseOptions.RecoveryModel;

            // Define a Backup object variable.   
            Backup backup = new Backup();

            // Specify the type of backup, the description, the name, and the database to be backed up.   
            backup.Action = BackupActionType.Database;
            backup.BackupSetDescription = $"Full backup of {connectionStringBuilder.InitialCatalog}";
            backup.BackupSetName = $"{connectionStringBuilder.InitialCatalog} Backup";
            backup.Database = connectionStringBuilder.InitialCatalog;

            var targetUniqueName = $"{GetMssqlTargetUniqueName(target)}";

            // Declare a BackupDeviceItem by supplying the backup device file name in the constructor, and the type of device is a file.   
            BackupDeviceItem backupDeviceItem = new BackupDeviceItem(targetUniqueName, DeviceType.File);

            // Add the device to the Backup object.   
            backup.Devices.Add(backupDeviceItem);
            // Set the Incremental property to False to specify that this is a full database backup.   
            backup.Incremental = false;

            // Set the expiration date.   
            DateTime backupdate = new DateTime(2099, 1, 1);
            backup.ExpirationDate = backupdate;

            // Specify that the log must be truncated after the backup is complete.   
            backup.LogTruncation = BackupTruncateLogType.Truncate;

            // Run SqlBackup to perform the full database backup on the instance of SQL Server.   
            backup.SqlBackup(srv);

            //// Remove the backup device from the Backup object.   
            backup.Devices.Remove(backupDeviceItem);

            // Set the database recovery mode back to its original value.  
            db.RecoveryModel = (RecoveryModel)recoveryModel;
            
            Utilities.Move(Path.Join(srv.BackupDirectory, targetUniqueName),
                Utilities.GetPathForCurrentUser(targetUniqueName));
            Utilities.Remove(Path.Join(srv.BackupDirectory, targetUniqueName));
        }

        private void MssqlIncrementalBackupAndRestore()
        {
            //var connectionStringBuilder = new SqlConnectionStringBuilder(target.Properties["ConnectionString"]);

            //Server srv = new Server(new ServerConnection(new SqlConnection(connectionStringBuilder.ConnectionString)));

            //Database db = srv.Databases[connectionStringBuilder.InitialCatalog];

            //// Store the current recovery model in a variable.   
            //int recoveryModel = (int)db.DatabaseOptions.RecoveryModel;

            //// Define a Backup object variable.   
            //Backup backup = new Backup();

            //// Specify the type of backup, the description, the name, and the database to be backed up.   
            //backup.Action = BackupActionType.Database;
            //backup.BackupSetDescription = "Full backup of Adventureworks2012";
            //backup.BackupSetName = "AdventureWorks2012 Backup";
            //backup.Database = connectionStringBuilder.InitialCatalog;

            //// Declare a BackupDeviceItem by supplying the backup device file name in the constructor, and the type of device is a file.   
            //BackupDeviceItem backupDeviceItem = default(BackupDeviceItem);
            //backupDeviceItem = new BackupDeviceItem(target.Type + ".bkp", DeviceType.File);

            //// Add the device to the Backup object.   
            //backup.Devices.Add(backupDeviceItem);
            //// Set the Incremental property to False to specify that this is a full database backup.   
            //backup.Incremental = false;

            //// Set the expiration date.   
            //DateTime backupdate = new DateTime(2099, 1, 1);
            //backup.ExpirationDate = backupdate;

            //// Specify that the log must be truncated after the backup is complete.   
            //backup.LogTruncation = BackupTruncateLogType.Truncate;

            //// Run SqlBackup to perform the full database backup on the instance of SQL Server.   
            //backup.SqlBackup(srv);

            ////// Remove the backup device from the Backup object.   
            //backup.Devices.Remove(backupDeviceItem);

            //// Make a change to the database, in this case, add a table called test_table.   
            //Table t = default(Table);
            //t = new Table(db, "test_table");
            //Column c = default(Column);
            //c = new Column(t, "col", DataType.Int);
            //t.Columns.Add(c);
            //t.Create();

            //// Create another file device for the differential backup and add the Backup object.   
            //BackupDeviceItem bdid = default(BackupDeviceItem);
            //bdid = new BackupDeviceItem("Test_Differential_Backup1", DeviceType.File);

            //// Add the device to the Backup object.   
            //bk.Devices.Add(bdid);

            //// Set the Incremental property to True for a differential backup.   
            //bk.Incremental = true;

            //// Run SqlBackup to perform the incremental database backup on the instance of SQL Server.   
            //bk.SqlBackup(srv);

            //// Inform the user that the differential backup is complete.   
            //System.Console.WriteLine("Differential Backup complete.");

            //// Remove the device from the Backup object.   
            //bk.Devices.Remove(bdid);

            //// Delete the AdventureWorks2012 database before restoring it  
            //// db.Drop();  

            //// Define a Restore object variable.  
            //Restore rs = new Restore();

            //// Set the NoRecovery property to true, so the transactions are not recovered.   
            //rs.NoRecovery = true;

            //// Add the device that contains the full database backup to the Restore object.   
            //rs.Devices.Add(bdi);

            //// Specify the database name.   
            //rs.Database = "AdventureWorks2012";

            //// Restore the full database backup with no recovery.   
            //rs.SqlRestore(srv);

            //// Inform the user that the Full Database Restore is complete.   
            //Console.WriteLine("Full Database Restore complete.");

            //// reacquire a reference to the database  
            //db = srv.Databases["AdventureWorks2012"];

            //// Remove the device from the Restore object.  
            //rs.Devices.Remove(bdi);

            //// Set the NoRecovery property to False.   
            //rs.NoRecovery = false;

            //// Add the device that contains the differential backup to the Restore object.   
            //rs.Devices.Add(bdid);

            //// Restore the differential database backup with recovery.   
            //rs.SqlRestore(srv);

            //// Inform the user that the differential database restore is complete.   
            //System.Console.WriteLine("Differential Database Restore complete.");

            //// Remove the device.   
            //rs.Devices.Remove(bdid);

            // Set the database recovery mode back to its original value.  
            //db.RecoveryModel = (RecoveryModel)recoveryModel;

            //// Drop the table that was added.   
            //db.Tables["test_table"].Drop();
            //db.Alter();

            //// Remove the backup files from the hard disk.  
            //// This location is dependent on the installation of SQL Server  
            //System.IO.File.Delete("C:\\Program Files\\Microsoft SQL Server\\MSSQL12.MSSQLSERVER\\MSSQL\\Backup\\Test_Full_Backup1");
            //System.IO.File.Delete("C:\\Program Files\\Microsoft SQL Server\\MSSQL12.MSSQLSERVER\\MSSQL\\Backup\\Test_Differential_Backup1");
        }

        private void RunUnpack()
        {
            foreach (var target in _configuration.Targets.Where(t => t.Unpack != null && t.Unpack.Enable))
            {
                switch (target.Type)
                {
                    case "mssql":
                        RunMssqlUnpack(target); break;
                    default: LogAndThrow($"Unknown target type {target}"); break;
                }
            }
        }

        private void RunMssqlUnpack(SnapConfiguration.Target target)
        {
            var connectionStringBuilder = new SqlConnectionStringBuilder(target.Properties["ConnectionString"]);

            Server srv = new Server(new ServerConnection(new SqlConnection(connectionStringBuilder.ConnectionString)));

            Database database = srv.Databases[connectionStringBuilder.InitialCatalog];

            // Store the current recovery model in a variable.   
            int recoveryModel = (int)database.DatabaseOptions.RecoveryModel;

            // Declare a BackupDeviceItem by supplying the backup device file name in the constructor, and the type of device is a file.   
            BackupDeviceItem backupDeviceItem = new BackupDeviceItem(target.Type + ".bkp", DeviceType.File);

            // Delete the AdventureWorks2012 database before restoring it  
            database.Drop();

            // Define a Restore object variable.  
            Restore restore = new Restore();

            // Set the NoRecovery property to true, so the transactions are not recovered.   
            restore.NoRecovery = true;

            // Add the device that contains the full database backup to the Restore object.   
            restore.Devices.Add(backupDeviceItem);

            // Specify the database name.   
            restore.Database = connectionStringBuilder.InitialCatalog;

            // Restore the full database backup with no recovery.   
            restore.SqlRestore(srv);

            // Inform the user that the Full Database Restore is complete.   
            Console.WriteLine("Full Database Restore complete.");

            // reacquire a reference to the database  
            database = srv.Databases[connectionStringBuilder.InitialCatalog];

            // Remove the device from the Restore object.  
            restore.Devices.Remove(backupDeviceItem);

            // Set the NoRecovery property to False.   
            restore.NoRecovery = false;

            // Set the database recovery mode back to its original value.  
            database.RecoveryModel = (RecoveryModel)recoveryModel;
        }

        private void RunClean()
        {

        }

        private void LogAndThrow(string message)
        {
            _environment.LogFatal(message);
            throw new SnapException(message, true);
        }
    }

    public class SnapConfiguration
    {
        public string ConfigurationDirectory { get; set; }
        public string ConfigurationFile { get; set; }
        public List<Target> Targets { get; set; }
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
        public class Target
        {
            public string Type { get; set; }
            public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();
            public PackTask Pack { get; set; }
            public UnpackTask Unpack { get; set; }
            public CleanTask Clean { get; set; }

        }

        public class PackTask
        {
            public bool Enable { get; set; }
            public string TargetDir { get; set; }
        }

        public class UnpackTask
        {
            public bool Enable { get; set; }
            public string TargetDir { get; set; }
        }

        public class CleanTask
        {
            public bool Enable { get; set; }
        }
    }

    class SnapException : Exception
    {
        public bool Handled { get; }
        public int ExitCode { get; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <param name="handled">The exception was already handled upstream. Try not throwing if possible.</param>
        public SnapException(string message, bool handled = false, int exitCode = -1)
        {
            Handled = handled;
            ExitCode = exitCode;
        }
    }

    public static class Utilities
    {
        public static void Remove(string from)
        {
            if (File.Exists(from))
            {
                File.Delete(from);
            }
        }
        public static void Move(string from, string to)
        {
            File.Move(from, to, true);
        }
        public static string GetPathForCurrentUser(string fileName = null)
        {
            var rootPath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "snap");
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

            return rootPath + (!string.IsNullOrWhiteSpace(fileName) ? fileName : string.Empty);
        }
    }
}
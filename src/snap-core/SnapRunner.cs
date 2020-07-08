using System;
using System.Data.Common;
using System.IO;
using System.Linq;
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

namespace Snap.Core
{
    public class SnapRunner
    {
        private readonly IEnvironmentBuilder _environment;
        private readonly IServiceProvider _serviceProvider;
        private Targets _targets;
        private SnapConfiguration _configuration;

        private SnapRunner(IEnvironmentBuilder environment, IServiceProvider serviceProvider)
        {
            _environment = environment;
            _serviceProvider = serviceProvider;

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

            var settings = new SnapConfiguration(Path.GetDirectoryName(absoluteRoute),Path.GetFileName(absoluteRoute));
            new ConfigurationBuilder()
                .AddJsonFile(absoluteRoute)
                .Build().Bind(settings);
            
            return settings;
        }
        public static SnapRunner Create()
        {
            var serviceCollection = new ServiceCollection();
            Configure(serviceCollection);

            return new SnapRunner(EnvironmentManager.Create(cfg =>
                cfg
                    .WithEnvironmentVariablePrefix("SNAP_")
                    .WithConsoleLogger()
                    .WithJsonFile("snap.json")
                ), serviceCollection.BuildServiceProvider());
        }

        private static void Configure(ServiceCollection serviceCollection)
        {
            
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
                switch (target.Type.ToLower())
                {
                    case "mssql":
                        RunMssqlPack(target);
                        break;
                    case "elasticsearch":
                        RunElasticSearchPack(target);
                        break;
                    default: LogAndThrow($"Unknown target type {target}"); break;
                }
            }
        }

        private void RunMssqlPack(SnapConfiguration.Target target)
        {
            var connectionStringBuilder = new SqlConnectionStringBuilder(target.GetConnectionStringProperty());

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

            var targetUniqueName = $"{_configuration.GenerateTargetUniqueName(target)}.bkp";

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

            if (target.IsRunningInDocker)
            {
                FileSystemUtils.MoveVirtual(Path.Join(srv.BackupDirectory, targetUniqueName),
                    FileSystemUtils.FileSystemType.Docker, FileSystemUtils.GeneratePathForCurrentUser(targetUniqueName),
                    FileSystemUtils.FileSystemType.Local, target.GetContainerIdProperty());
            }
            else
            {
                FileSystemUtils.Move(Path.Join(srv.BackupDirectory, targetUniqueName),
                    FileSystemUtils.GeneratePathForCurrentUser(targetUniqueName));
                FileSystemUtils.Remove(Path.Join(srv.BackupDirectory, targetUniqueName));
            }
        }

        private void RunElasticSearchPack(SnapConfiguration.Target target)
        {
            var elastic = new ElasticClient(new Uri(target.GetHostProperty()));
            //elastic.Snapshot.ver
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
            var connectionStringBuilder = new SqlConnectionStringBuilder(target.GetConnectionStringProperty());

            Server srv = new Server(new ServerConnection(new SqlConnection(connectionStringBuilder.ConnectionString)));

            Database database = srv.Databases[connectionStringBuilder.InitialCatalog];

            // Store the current recovery model in a variable.   
            int recoveryModel = (int)database.DatabaseOptions.RecoveryModel;

            var targetUniqueName = $"{_configuration.GenerateTargetUniqueName(target)}.bkp";

            if (target.IsRunningInDocker)
            {
                FileSystemUtils.MoveVirtual(FileSystemUtils.GeneratePathForCurrentUser(targetUniqueName),
                    FileSystemUtils.FileSystemType.Local, Path.Join(srv.BackupDirectory, targetUniqueName),
                    FileSystemUtils.FileSystemType.Docker, target.GetContainerIdProperty());
            }
            else
            {
                FileSystemUtils.Move(FileSystemUtils.GeneratePathForCurrentUser(targetUniqueName), Path.Join(srv.BackupDirectory, targetUniqueName));
            }
            
            // Declare a BackupDeviceItem by supplying the backup device file name in the constructor, and the type of device is a file.   
            BackupDeviceItem backupDeviceItem = new BackupDeviceItem(targetUniqueName, DeviceType.File);

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

            //FileSystemUtils.Remove(FileSystemUtils.GeneratePathForCurrentUser(targetUniqueName));
        }

        private void RunElasticvSearchUnpack(SnapConfiguration.Target target)
        {

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
}
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;

namespace Snap.Core.Runners
{
    public class MssqlTargetRunner : ITargetRunner
    {
        private readonly SnapConfiguration _configuration;

        public MssqlTargetRunner(SnapConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string Type => "mssql";

        public void Pack(SnapConfiguration.Target target)
        {
            var connectionStringBuilder = new SqlConnectionStringBuilder(target.GetConnectionStringProperty());

            Server srv = new Server(new ServerConnection(new SqlConnection(connectionStringBuilder.ConnectionString)));
            Database db = srv.Databases[connectionStringBuilder.InitialCatalog];
            int recoveryModel = (int)db.DatabaseOptions.RecoveryModel;
            Backup backup = new Backup();
            backup.Action = BackupActionType.Database;
            backup.BackupSetDescription = $"Full backup of {connectionStringBuilder.InitialCatalog}";
            backup.BackupSetName = $"{connectionStringBuilder.InitialCatalog} Backup";
            backup.Database = connectionStringBuilder.InitialCatalog;

            var targetUniqueName = $"{_configuration.GenerateTargetUniqueName(target)}.bkp";
            
            BackupDeviceItem backupDeviceItem = new BackupDeviceItem(targetUniqueName, DeviceType.File);
            backup.Devices.Add(backupDeviceItem);
            backup.Incremental = false;
            DateTime backupdate = new DateTime(2099, 1, 1);
            backup.ExpirationDate = backupdate;
            backup.LogTruncation = BackupTruncateLogType.Truncate;
            backup.SqlBackup(srv);
            backup.Devices.Remove(backupDeviceItem);
            db.RecoveryModel = (RecoveryModel)recoveryModel;

            if (target.GetIsRunningInDockerContainer())
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

        public void Restore(SnapConfiguration.Target target)
        {
            var connectionStringBuilder = new SqlConnectionStringBuilder(target.GetConnectionStringProperty());
            Server srv = new Server(new ServerConnection(new SqlConnection(connectionStringBuilder.ConnectionString)));
            Database database = srv.Databases[connectionStringBuilder.InitialCatalog];
            int recoveryModel = (int)database.DatabaseOptions.RecoveryModel;
            
            var targetUniqueName = $"{_configuration.GenerateTargetUniqueName(target)}.bkp";

            if (target.GetIsRunningInDockerContainer())
            {
                FileSystemUtils.MoveVirtual(FileSystemUtils.GeneratePathForCurrentUser(targetUniqueName),
                    FileSystemUtils.FileSystemType.Local, Path.Join(srv.BackupDirectory, targetUniqueName),
                    FileSystemUtils.FileSystemType.Docker, target.GetContainerIdProperty());
            }
            else
            {
                FileSystemUtils.Move(FileSystemUtils.GeneratePathForCurrentUser(targetUniqueName), Path.Join(srv.BackupDirectory, targetUniqueName));
            }
            
            BackupDeviceItem backupDeviceItem = new BackupDeviceItem(targetUniqueName, DeviceType.File);
            database.Drop();
            Restore restore = new Restore(); 
            restore.NoRecovery = true;
            restore.Devices.Add(backupDeviceItem);
            restore.Database = connectionStringBuilder.InitialCatalog;
            restore.SqlRestore(srv);
            database = srv.Databases[connectionStringBuilder.InitialCatalog];
            restore.Devices.Remove(backupDeviceItem);
            restore.NoRecovery = false;
            database.RecoveryModel = (RecoveryModel)recoveryModel;

            //FileSystemUtils.Remove(FileSystemUtils.GeneratePathForCurrentUser(targetUniqueName));
        }

        public void Clean(SnapConfiguration.Target target)
        {
            throw new NotImplementedException();
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
    }
}

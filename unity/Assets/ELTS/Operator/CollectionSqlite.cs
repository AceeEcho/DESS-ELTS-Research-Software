#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Elts.Operator
{
    /// <summary>Small parameterized SQLite connection. Windows supplies the native library;
    /// no database service or development tools are needed by the player.</summary>
    internal sealed class CollectionSqlite : IDisposable
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || ELTS_WINDOWS_SQLITE
        private const string Library="winsqlite3";
#else
        private const string Library="sqlite3";
#endif
        private IntPtr handle;
        private static byte[] Utf8(string text)=>Encoding.UTF8.GetBytes(text+"\0");
        [DllImport(Library)] private static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
        [DllImport(Library)] private static extern int sqlite3_close_v2(IntPtr db);
        [DllImport(Library)] private static extern IntPtr sqlite3_errmsg(IntPtr db);
        [DllImport(Library)] private static extern int sqlite3_busy_timeout(IntPtr db,int milliseconds);
        [DllImport(Library)] private static extern int sqlite3_prepare_v2(IntPtr db,byte[] sql,int bytes,out IntPtr statement,IntPtr tail);
        [DllImport(Library)] private static extern int sqlite3_bind_text(IntPtr statement,int index,byte[] value,int bytes,IntPtr destructor);
        [DllImport(Library)] private static extern int sqlite3_step(IntPtr statement);
        [DllImport(Library)] private static extern int sqlite3_finalize(IntPtr statement);
        [DllImport(Library)] private static extern int sqlite3_column_count(IntPtr statement);
        [DllImport(Library)] private static extern IntPtr sqlite3_column_text(IntPtr statement,int column);
        [DllImport(Library)] private static extern IntPtr sqlite3_column_name(IntPtr statement,int column);
        [DllImport(Library)] private static extern IntPtr sqlite3_backup_init(IntPtr destination,byte[] destinationName,IntPtr source,byte[] sourceName);
        [DllImport(Library)] private static extern int sqlite3_backup_step(IntPtr backup,int pages);
        [DllImport(Library)] private static extern int sqlite3_backup_finish(IntPtr backup);
        private static string Text(IntPtr pointer)=>pointer==IntPtr.Zero?"":Marshal.PtrToStringUTF8(pointer)??"";
        private void Check(int code) { if(code!=0)throw new IOException("SQLite: "+Text(sqlite3_errmsg(handle))+" ("+code+")"); }
        public CollectionSqlite(string path)
        {
            int code=sqlite3_open_v2(Utf8(path),out handle,6,IntPtr.Zero);
            if(code!=0){string message=Text(sqlite3_errmsg(handle));Dispose();throw new IOException("Cannot open collection database: "+message);}
            Check(sqlite3_busy_timeout(handle,5000));
            Execute("PRAGMA foreign_keys=ON");
        }
        public List<Dictionary<string,string>> Query(string sql,params string[] values)
        {
            Check(sqlite3_prepare_v2(handle,Utf8(sql),-1,out var statement,IntPtr.Zero));
            try
            {
                for(int i=0;i<values.Length;i++) { var bytes=Utf8(values[i]);Check(sqlite3_bind_text(statement,i+1,bytes,bytes.Length-1,new IntPtr(-1))); }
                var rows=new List<Dictionary<string,string>>();int code;
                while((code=sqlite3_step(statement))==100)
                {
                    var row=new Dictionary<string,string>();
                    for(int i=0;i<sqlite3_column_count(statement);i++)row[Text(sqlite3_column_name(statement,i))]=Text(sqlite3_column_text(statement,i));
                    rows.Add(row);
                }
                if(code!=101)Check(code);
                return rows;
            }
            finally { sqlite3_finalize(statement); }
        }
        public void Execute(string sql,params string[] values)=>Query(sql,values);
        public void Backup(string destination)
        {
            using(var target=new CollectionSqlite(destination))
            {
                var backup=sqlite3_backup_init(target.handle,Utf8("main"),handle,Utf8("main"));
                if(backup==IntPtr.Zero)throw new IOException("Could not start database snapshot.");
                int result;
                try { result=sqlite3_backup_step(backup,-1); }
                finally { Check(sqlite3_backup_finish(backup)); }
                if(result!=101)throw new IOException("Database snapshot failed: "+result);
                target.Execute("PRAGMA journal_mode=DELETE");
            }
        }
        public void Dispose(){if(handle!=IntPtr.Zero){sqlite3_close_v2(handle);handle=IntPtr.Zero;}}
    }
}

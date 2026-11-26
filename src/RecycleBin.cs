using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace BlueStar
{
    public sealed class RecycledFile
    {
        public string OriginalPath { get; set; }
        public string BinPath { get; set; }
    }
    public sealed class AbsorbedBatch
    {
        public RecycledFile[] Files { get; set; }
    }
    // Ask the source folder's shell provider specifically for a recycle operation.
    // A provider that cannot recycle the item returns an error.
    public static class RecycleBin
    {
        [DllImport("shell32.dll")]
        static extern int SHQueryRecycleBinW(IntPtr root,[In,Out] byte[] info);
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
        internal static extern int SHEmptyRecycleBinW(IntPtr owner,string root,uint flags);

        internal static long? ItemCount()
        {
            // SHQUERYRBINFO has a DWORD and two 64-bit values, aligned on Win64.
            var info=new byte[IntPtr.Size==8?24:20];
            info[0]=(byte)info.Length;
            return SHQueryRecycleBinW(IntPtr.Zero,info)==0?(long?)BitConverter.ToInt64(info,info.Length-8):null;
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out ITransferSource source);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint kind,[MarshalAs(UnmanagedType.LPWStr)] out string name);
        }

        [ComImport, Guid("00ADB003-BDE9-45C6-8E29-D09F9353E108"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITransferSource
        {
            void Advise(IntPtr sink, out uint cookie);
            void Unadvise(uint cookie);
            void SetProperties(IntPtr properties);
            void OpenItem(IShellItem item, uint flags, ref Guid iid, out IntPtr result);
            void MoveItem(IShellItem item, IShellItem parent, string name, uint flags, out IShellItem result);
            void RecycleItem(IShellItem item, IShellItem destination, uint flags, out IShellItem result);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItem item);

        public static void Send(string path)
        {
            SendTracked(path);
        }
        internal static RecycledFile SendTracked(string path)
        {
            Guid iid = typeof(IShellItem).GUID;
            Guid transfer = new Guid("D5E346A1-F753-4932-B403-4574800E2498");
            Guid sourceType = typeof(ITransferSource).GUID;
            IShellItem item = null, parent = null, bin = null, result = null;
            ITransferSource source = null;
            try
            {
                SHCreateItemFromParsingName(Path.GetFullPath(path), IntPtr.Zero, ref iid, out item);
                item.GetParent(out parent);
                parent.BindToHandler(IntPtr.Zero, ref transfer, ref sourceType, out source);
                SHCreateItemFromParsingName("shell:RecycleBinFolder", IntPtr.Zero, ref iid, out bin);
                source.RecycleItem(item, bin, 0, out result);
                if (result == null) throw new IOException("系统未确认此项目已进入回收站。");
                string binPath=null;
                try { result.GetDisplayName(0x80058000,out binPath); } // SIGDN_FILESYSPATH of the exact recycled item
                catch(COMException) { } // The shell may recycle an item without exposing a filesystem identity.
                return new RecycledFile { OriginalPath=path,BinPath=binPath };
            }
            finally
            {
                foreach (object obj in new object[] { result, bin, source, parent, item })
                    if (obj != null) Marshal.ReleaseComObject(obj);
            }
        }

        internal static Task<T> OnShellThread<T>(Func<T> action)
        {
            var done=new TaskCompletionSource<T>();
            var worker=new Thread(delegate() {
                try { done.SetResult(action()); }
                catch(Exception ex) { done.SetException(ex); }
            });
            worker.SetApartmentState(ApartmentState.STA); worker.Start(); return done.Task;
        }

        static bool Exists(string path) { return File.Exists(path) || Directory.Exists(path); }

        // Match the shell's recycled identity, since separate deletions can share an original name.
        internal static string[] RestoreLatest(List<AbsorbedBatch> history)
        {
            if(history.Count==0) return new string[0];
            var wanted=new HashSet<string>(history.SelectMany(b=>b.Files).Select(f=>f.BinPath).Where(p=>!String.IsNullOrEmpty(p)),StringComparer.OrdinalIgnoreCase);
            var found=new Dictionary<string,object>(StringComparer.OrdinalIgnoreCase);
            dynamic shell=null,bin=null,items=null;
            try
            {
                Type shellType=Type.GetTypeFromProgID("Shell.Application");
                if(shellType==null) return new string[0];
                shell=Activator.CreateInstance(shellType);
                bin=shell.NameSpace(10);
                if(bin==null) return new string[0];
                items=bin.Items();
                int count=items.Count;
                for(int i=0;i<count && wanted.Count>0;i++)
                {
                    dynamic item=items.Item(i);
                    if(item==null) continue;
                    try
                    {
                        string key=item.Path;
                        if(!String.IsNullOrEmpty(key) && wanted.Remove(key)) { found.Add(key,(object)item); item=null; }
                    }
                    finally { if(item!=null) Marshal.ReleaseComObject((object)item); }
                }
                while(history.Count>0)
                {
                    var batch=history[history.Count-1];
                    var rest=new List<RecycledFile>();
                    var restored=new List<string>();
                    foreach(var file in batch.Files)
                    {
                        object item;
                        if(file.BinPath==null || !found.TryGetValue(file.BinPath,out item)) continue;
                        if(RestoreItem(item,file)) restored.Add(file.OriginalPath);
                        else rest.Add(file);
                    }
                    if(rest.Count==0) history.RemoveAt(history.Count-1);
                    else batch.Files=rest.ToArray();
                    if(rest.Count>0 || restored.Count>0) return restored.ToArray();
                }
                return new string[0];
            }
            finally
            {
                foreach(object item in found.Values) Marshal.ReleaseComObject(item);
                foreach(object obj in new object[]{items,bin,shell}) if(obj!=null) Marshal.ReleaseComObject(obj);
            }
        }
        static bool RestoreItem(dynamic item,RecycledFile file)
        {
            try
            {
                string parent=item.ExtendedProperty("System.Recycle.DeletedFrom");
                string name=item.ExtendedProperty("System.FileName");
                if(String.IsNullOrEmpty(parent) || String.IsNullOrEmpty(name) ||
                   !String.Equals(Path.Combine(parent,name),file.OriginalPath,StringComparison.OrdinalIgnoreCase) ||
                   Exists(file.OriginalPath) || !Directory.Exists(parent)) return false;
                item.InvokeVerb("undelete");
                for(int i=0;i<100;i++)
                {
                    if(!Exists(file.BinPath) && Exists(file.OriginalPath)) return true;
                    Thread.Sleep(30);
                }
            }
            catch(COMException) { }
            return false;
        }
    }

    // Measure once on entry. A committed drop uses the same per-item measurements.
    sealed class DropBatch : IDisposable
    {
        public readonly string[] Paths;
        public readonly Task<long[]> Sizes;
        readonly CancellationTokenSource cancel=new CancellationTokenSource();

        public DropBatch(string[] paths)
        {
            Paths=paths;
            var token=cancel.Token;
            Sizes=Task.Run(()=>paths.Select(path=>Measure(path,token)).ToArray(),token);
        }
        public void Dispose() { cancel.Cancel(); cancel.Dispose(); }

        static long Measure(string path,CancellationToken token)
        {
            long bytes=0;
            var pending=new Stack<string>(); pending.Push(path);
            while(pending.Count>0)
            {
                token.ThrowIfCancellationRequested();
                string item=pending.Pop();
                try
                {
                    var attributes=File.GetAttributes(item);
                    if((attributes&FileAttributes.ReparsePoint)!=0) continue;
                    if((attributes&FileAttributes.Directory)==0) bytes+=new FileInfo(item).Length;
                    else foreach(string child in Directory.EnumerateFileSystemEntries(item))
                    { token.ThrowIfCancellationRequested(); pending.Push(child); }
                }
                catch(IOException) { } // Raced a move, or part of a folder was unreadable.
                catch(UnauthorizedAccessException) { }
                catch(System.Security.SecurityException) { }
            }
            return bytes;
        }

        public static double Weight(long bytes)
        {
            return Math.Min(1,Math.Log(1+bytes/65536.0,2)/15);
        }
    }
}

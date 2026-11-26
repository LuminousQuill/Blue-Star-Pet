using System;
using System.IO;
using Microsoft.Win32;

namespace BlueStar
{
    static class StartupAtLogin
    {
        const string Name="碧蓝新星";
        const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovalKey=@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        static readonly string Command="\""+typeof(StartupAtLogin).Assembly.Location+"\"";

        static bool DisabledByWindows()
        {
            using(var key=Registry.CurrentUser.OpenSubKey(ApprovalKey))
            {
                var state=key==null?null:key.GetValue(Name) as byte[];
                return state!=null && state.Length>0 && (state[0]&1)!=0;
            }
        }
        public static bool IsEnabled()
        {
            using(var key=Registry.CurrentUser.OpenSubKey(RunKey))
                return key!=null && string.Equals(key.GetValue(Name) as string,Command,StringComparison.OrdinalIgnoreCase)
                    && !DisabledByWindows();
        }
        public static void SetEnabled(bool enabled)
        {
            if(!enabled)
            {
                using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true))
                    if(key!=null) key.DeleteValue(Name,false);
                return;
            }
            if(Command.Length>260) throw new PathTooLongException("桌宠路径过长，请移至较短的路径后重试。");
            using(var key=Registry.CurrentUser.CreateSubKey(RunKey))
                key.SetValue(Name,Command,RegistryValueKind.String);
            // Re-enable only this entry when the user explicitly turns it on here.
            if(DisabledByWindows())
                using(var key=Registry.CurrentUser.OpenSubKey(ApprovalKey,true))
                    if(key!=null) key.DeleteValue(Name,false);
        }
    }
}

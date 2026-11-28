using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BlueStar
{
    // The system viewer owns playback, chrome and lifetime. We borrow only an identifiable top-level HWND.
    sealed class PinnedViewer : IDisposable
    {
        static readonly HashSet<string> media=new HashSet<string>((
            ".jpg .jpeg .jfif .jpe .png .apng .gif .bmp .dib .tif .tiff .webp .heic .heif .avif .jxl .ico .svg " +
            ".mp4 .m4v .mkv .webm .mov .avi .wmv .mpg .mpeg .mpe .m2v .m2ts .mts .ts .3gp .3g2 .ogv .flv .asf .vob .rm .rmvb " +
            ".mp3 .wav .flac .m4a .aac .ogg .oga .opus .wma .aif .aiff .alac .mka .m4b .ape .ac3 .amr .mid .midi").Split(' '),
            StringComparer.OrdinalIgnoreCase);

        public static bool IsMedia(string path) { return !String.IsNullOrEmpty(path) && media.Contains(Path.GetExtension(path)); }

        public IntPtr Handle { get; private set; }
        readonly uint pid,thread;
        bool? originalTopmost;
        bool disposed;
        Rect lastBounds,gestureStart=Rect.Empty;
        Gesture gesture;
        bool leftWasDown,pointerPressed;
        int matchingSteps;
        Point pressPoint,samplePoint;
        Rect pressBounds,sampleBounds;
        internal enum Gesture { None, Pending, Moving, Sizing }
        static readonly int guiInfoSize=Marshal.SizeOf(typeof(GuiInfo));

        PinnedViewer(IntPtr hwnd)
        {
            Handle=hwnd;
            thread=GetWindowThreadProcessId(hwnd,out pid);
            lastBounds=Bounds;
        }

        public static PinnedViewer WindowAtPress(IntPtr pet)
        {
            NativePoint p;
            if(!GetPhysicalCursorPos(out p)) return null;
            IntPtr w=GetAncestor(WindowFromPhysicalPoint(p),2);
            if(w==IntPtr.Zero || w==pet || !IsWindowVisible(w) || GetWindow(w,4)!=IntPtr.Zero ||
                (GetWindowLong(w,-20)&0x80)!=0) return null;
            var viewer=new PinnedViewer(w);
            viewer.ObserveButton(true,w,new Point(p.X,p.Y),viewer.lastBounds);
            return viewer;
        }
        public bool CanCatchOpenWindow()
        {
            if(!Alive || Minimized || Maximized) return false;
            string cls=ClassName(Handle);
            if(cls=="#32770" || cls=="MozillaWindowClass") return false;
            if(cls=="ApplicationFrameWindow")
            {
                bool match=false;
                EnumChildWindows(Handle,delegate(IntPtr child,IntPtr arg) {
                    if(ClassName(child)!="Windows.UI.Core.CoreWindow") return true;
                    uint childPid; GetWindowThreadProcessId(child,out childPid);
                    match=IsOpenMediaProcess(childPid,cls);
                    return !match;
                },IntPtr.Zero);
                return match;
            }
            return IsOpenMediaProcess(pid,cls);
        }

        static bool IsOpenMediaProcess(uint process,string cls)
        {
            IntPtr h=OpenProcess(0x1000,false,process);
            if(h==IntPtr.Zero) return false;
            string path,appId;
            try
            {
                var b=new StringBuilder(1024); int size=b.Capacity;
                if(!QueryFullProcessImageName(h,0,b,ref size)) return false;
                path=b.ToString(); b.Length=0;uint length=(uint)b.Capacity;
                appId=GetApplicationUserModelId(h,ref length,b)==0?b.ToString():"";
            }
            finally { CloseHandle(h); }
            string name=Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            if(name=="explorer") return cls=="CabinetWClass" || cls=="ExploreWClass";
            if(BrowserOrShell(name)) return false;
            return IsDedicatedViewer(name,appId) || HasMediaProductIdentity(appId,path);
        }

        public static Task<PinnedViewer> OpenAsync(string path)
        {
            return Task.Run(delegate {
                if(!IsMedia(path)) throw new IOException("不支持的媒体文件类型。");
                if(!File.Exists(path)) throw new FileNotFoundException("媒体文件不存在。",path);
                string ext=Path.GetExtension(path);
                string appId=Association(ext,21),exe=Association(ext,2);
                var before=new Dictionary<IntPtr,string>();
                EnumWindows(delegate(IntPtr w,IntPtr unused) { before[w]=Title(w); return true; },IntPtr.Zero);
                uint launched=0;
                // A shell activation can succeed with no process handle (single-instance players and packaged apps).
                using(var process=Process.Start(new ProcessStartInfo(Path.GetFullPath(path)) {UseShellExecute=true}))
                    if(process!=null) launched=(uint)process.Id;
                if(appId.Length==0 && BrowserOrShell(Path.GetFileNameWithoutExtension(exe))) return null;
                bool chromiumMedia=HasMediaProductIdentity(appId,exe);

                var stable=new Stopwatch();
                IntPtr last=IntPtr.Zero;
                var elapsed=Stopwatch.StartNew();
                do
                {
                    IntPtr candidate=FindViewer(path,appId,exe,launched,before,chromiumMedia);
                    if(candidate!=last) { last=candidate; stable.Restart(); }
                    if(last!=IntPtr.Zero && stable.ElapsedMilliseconds>=450) return new PinnedViewer(last);
                    Thread.Sleep(120);
                } while(elapsed.ElapsedMilliseconds<9000);
                return null;
            });
        }

        static string Association(string ext,uint field)
        {
            var value=new StringBuilder(2048);
            uint length=(uint)value.Capacity;
            return AssocQueryString(0,field,ext,null,value,ref length)==0?value.ToString():"";
        }

        static IntPtr FindViewer(string path,string appId,string exe,uint launched,Dictionary<IntPtr,string> before,bool chromiumMedia)
        {
            IntPtr best=IntPtr.Zero,foreground=GetForegroundWindow();
            int bestScore=0;
            string file=Path.GetFileName(path);
            EnumWindows(delegate(IntPtr w,IntPtr unused) {
                if(!IsWindowVisible(w) || GetWindow(w,4)!=IntPtr.Zero || (GetWindowLong(w,-20)&0x80)!=0) return true;
                int cloaked;
                if(DwmGetWindowAttribute(w,14,out cloaked,4)==0 && cloaked!=0) return true;
                string cls=ClassName(w);
                bool chromium=cls.StartsWith("Chrome_WidgetWin",StringComparison.Ordinal);
                if(cls=="#32770" || cls=="MozillaWindowClass" || (chromium && !chromiumMedia)) return true;
                string title=Title(w);
                if(String.IsNullOrWhiteSpace(title)) return true;
                uint process; GetWindowThreadProcessId(w,out process);
                string image;
                if(cls=="ApplicationFrameWindow")
                {
                    // Classic UWP windows belong to ApplicationFrameHost; their CoreWindow belongs to the app.
                    if(String.IsNullOrEmpty(appId)) return true;
                    image="";
                    bool found=false;
                    EnumChildWindows(w,delegate(IntPtr child,IntPtr arg) {
                        if(ClassName(child)!="Windows.UI.Core.CoreWindow") return true;
                        uint childPid; GetWindowThreadProcessId(child,out childPid);
                        string childImage;
                        found=MatchesProcess(childPid,appId,"",0,out childImage);
                        if(found) image=childImage;
                        return !found;
                    },IntPtr.Zero);
                    if(!found) return true;
                }
                else if(!MatchesProcess(process,appId,exe,chromium?0:launched,out image)) return true;
                if(BrowserOrShell(image)) return true;
                bool dedicated=IsDedicatedViewer(image,appId);
                string previous;
                bool fresh=!before.TryGetValue(w,out previous);
                bool named=NamesFile(title,file);
                if(chromium && !(fresh && named)) return true;
                int score=0;
                if(named && fresh) score=4;
                else if(named && dedicated && (title!=previous || w==foreground)) score=3;
                else if(fresh && dedicated && w==foreground && IsPlayerSurface(cls,image,appId)) score=1;
                if(score>bestScore) { best=w; bestScore=score; }
                else if(score!=0 && score==bestScore) best=IntPtr.Zero;
                return true;
            },IntPtr.Zero);
            return best;
        }

        static bool NamesFile(string title,string file)
        {
            // Some viewers omit the extension. Only accept the stem as a whole title or a delimited caption.
            foreach(string name in new[]{file,Path.GetFileNameWithoutExtension(file)})
            {
                if(String.IsNullOrEmpty(name)) continue;
                int at=title.IndexOf(name,StringComparison.OrdinalIgnoreCase);
                if(at<0) continue;
                string prefix=title.Substring(0,at).TrimEnd('[','"');
                string suffix=title.Substring(at+name.Length).TrimStart(']','"');
                bool start=prefix.Length==0 || prefix.EndsWith("\\");
                bool end=suffix.Length==0;
                foreach(string separator in new[]{" - "," — "," – "," | "," :: "," • "})
                {
                    start|=prefix.EndsWith(separator,StringComparison.Ordinal);
                    end|=suffix.StartsWith(separator,StringComparison.Ordinal);
                }
                if(start && end) return true;
            }
            return false;
        }

        static bool MatchesProcess(uint process,string appId,string exe,uint launched,out string name)
        {
            name="";
            IntPtr h=OpenProcess(0x1000,false,process);
            if(h==IntPtr.Zero) return false;
            try
            {
                var b=new StringBuilder(1024); int size=b.Capacity;
                string image=QueryFullProcessImageName(h,0,b,ref size)?b.ToString():"";
                name=Path.GetFileNameWithoutExtension(image).ToLowerInvariant();
                if(name.Length==0) return false;
                if(!String.IsNullOrEmpty(appId))
                {
                    b.Length=0; uint length=(uint)b.Capacity;
                    if(GetApplicationUserModelId(h,ref length,b)==0 && String.Equals(b.ToString(),appId,StringComparison.OrdinalIgnoreCase)) return true;
                }
                return (process==launched && process!=0) || String.Equals(image,exe,StringComparison.OrdinalIgnoreCase);
            }
            finally { CloseHandle(h); }
        }

        static bool BrowserOrShell(string image)
        {
            switch(image.ToLowerInvariant())
            {
                case "chrome": case "msedge": case "firefox": case "brave": case "vivaldi": case "opera": case "iexplore":
                case "explorer": case "openwith": case "dllhost": case "applicationframehost": case "rundll32": case "browser": return true;
                default: return false;
            }
        }

        static bool HasMediaProductIdentity(string appId,string exe)
        {
            // Product metadata also identifies media apps that share a browser's window class.
            if((exe.Length!=0 && String.Equals(exe,Association("https",2),StringComparison.OrdinalIgnoreCase)) ||
               (appId.Length!=0 && String.Equals(appId,Association("https",21),StringComparison.OrdinalIgnoreCase))) return false;
            string product=Path.GetFileNameWithoutExtension(exe)+" "+appId;
            if(File.Exists(exe))
            {
                try
                {
                    var info=FileVersionInfo.GetVersionInfo(exe);
                    product+=" "+info.ProductName+" "+info.FileDescription;
                }
                catch(IOException) {}
                catch(UnauthorizedAccessException) {}
                catch(System.ComponentModel.Win32Exception) {}
            }
            product=product.Replace(" ","").ToLowerInvariant();
            if(product.Contains("browser") || product.Contains("浏览器")) return false;
            foreach(string name in new[]{"mediaplayer","musicplayer","videoplayer","audioplayer","imageviewer",
                "photoviewer","播放器","看图","图片查看器","qqmusic","cloudmusic","yesplaymusic"})
                if(product.Contains(name)) return true;
            return false;
        }

        static bool IsDedicatedViewer(string image,string appId)
        {
            if(appId.StartsWith("Microsoft.Windows.Photos_",StringComparison.OrdinalIgnoreCase) ||
               appId.StartsWith("Microsoft.Zune",StringComparison.OrdinalIgnoreCase)) return true;
            switch(image)
            {
                case "potplayermini": case "potplayermini64": case "vlc": case "mpv": case "mpvnet":
                case "mpc-hc": case "mpc-hc64": case "mpc-be": case "mpc-be64": case "wmplayer":
                case "i_view32": case "i_view64": case "honeyview": case "jpegview": case "imageglass": case "qview":
                case "xnview": case "xnviewmp": case "fsviewer": case "nomacs": case "foobar2000": case "aimp":
                case "musicbee": case "smplayer": return true;
                default: return false;
            }
        }

        static bool IsPlayerSurface(string cls,string image,string appId)
        {
            return cls.StartsWith("PotPlayer",StringComparison.Ordinal) || cls=="MediaPlayerClassicW" ||
                cls=="mpv" || cls=="WMPlayerApp" || (image=="vlc" && cls.StartsWith("Qt",StringComparison.Ordinal)) ||
                (appId.StartsWith("Microsoft.Zune",StringComparison.OrdinalIgnoreCase) &&
                (cls=="ApplicationFrameWindow" || cls=="WinUIDesktopWin32WindowClass"));
        }

        static string ClassName(IntPtr w) { var s=new StringBuilder(256); GetClassName(w,s,s.Capacity); return s.ToString(); }
        static string Title(IntPtr w) { var s=new StringBuilder(1024); GetWindowText(w,s,s.Capacity); return s.ToString(); }

        public bool Alive
        {
            get { uint current; return !disposed && IsWindow(Handle) && GetWindowThreadProcessId(Handle,out current)==thread && current==pid; }
        }
        public bool Minimized { get { return Alive && IsIconic(Handle); } }
        public bool Maximized { get { return Alive && IsZoomed(Handle); } }
        public bool CanResize { get { return Alive && (GetWindowLong(Handle,-16)&0x40000)!=0; } }
        public Rect Bounds { get { return ReadBounds(false); } }
        internal Rect NormalBounds { get { return ReadBounds(true); } }
        Rect ReadBounds(bool normalOnly)
        {
            NativeRect r;
            using(new PhysicalCoordinates(true))
                return Alive && IsWindowVisible(Handle) && (!normalOnly || (!IsIconic(Handle) && !IsZoomed(Handle))) &&
                    GetWindowRect(Handle,out r) && r.Right>r.Left && r.Bottom>r.Top
                    ? new Rect(r.Left,r.Top,r.Right-r.Left,r.Bottom-r.Top) : Rect.Empty;
        }

        public void RestoreForPin()
        {
            if(Alive && (IsIconic(Handle) || IsZoomed(Handle))) ShowWindow(Handle,4);
        }

        public bool PinTopmost()
        {
            if(!Alive) return false;
            bool pinned=(GetWindowLong(Handle,-20)&8)!=0;
            if(!originalTopmost.HasValue)
            {
                originalTopmost=pinned;
                leftWasDown=PetWindow.ReferenceButtonDown();
            }
            return pinned || (SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x213) && (GetWindowLong(Handle,-20)&8)!=0);
        }

        public bool Place(Rect bounds)
        {
            if(bounds.IsEmpty || NormalBounds.IsEmpty || !SetBounds(bounds,CanResize,false)) return false;
            pointerPressed=false; lastBounds=Bounds;
            return true;
        }
        // A foreign thread can be busy in its own render or message loop.
        internal bool RequestPlacement(Rect bounds,bool resize) { return SetBounds(bounds,resize,true); }
        bool SetBounds(Rect bounds,bool resize,bool async)
        {
            uint flags=0x214u|(resize?0u:1u)|(async?0x4000u:0u);
            using(PhysicalCoordinates.ForWindow(Handle))
                return SetWindowPos(Handle,IntPtr.Zero,(int)Math.Round(bounds.X),(int)Math.Round(bounds.Y),
                    (int)Math.Round(bounds.Width),(int)Math.Round(bounds.Height),flags);
        }

        public bool Foreground { get { return GetForegroundWindow()==Handle; } }

        void ObserveButton(bool down,IntPtr hit,Point cursor,Rect bounds)
        {
            if(down==leftWasDown) return;
            leftWasDown=down;
            pointerPressed=down && !bounds.IsEmpty && GetAncestor(hit,2)==Handle;
            matchingSteps=0;
            if(!pointerPressed) return;
            pressBounds=sampleBounds=bounds;
            pressPoint=samplePoint=cursor;
        }

        bool FollowsPointer(Rect now,Point cursor)
        {
            if(!pointerPressed) return false;
            if(now.IsEmpty || now.Size!=pressBounds.Size) { pointerPressed=false; return false; }
            Vector step=now.Location-sampleBounds.Location;
            if(step.LengthSquared>=4)
            {
                Vector mouseStep=cursor-samplePoint;
                Vector distance=now.Location-pressBounds.Location;
                Vector mouseDistance=cursor-pressPoint;
                bool follows=mouseStep.LengthSquared>=4 && Vector.Multiply(step,mouseStep)>=0.85*step.Length*mouseStep.Length &&
                    (step-mouseStep).Length<=4+step.Length*0.30 &&
                    (distance-mouseDistance).Length<=6+distance.Length*0.25;
                matchingSteps=follows?Math.Min(2,matchingSteps+1):0;
                sampleBounds=now; samplePoint=cursor;
            }
            return matchingSteps>=2;
        }

        internal Gesture ReadGesture(Rect now)
        {
            var info=new GuiInfo {Size=guiInfoSize};
            bool valid=!now.IsEmpty && GetGUIThreadInfo(thread,ref info);
            bool native=valid && (info.Flags&2)!=0 && info.MoveSize==Handle;
            // Skinned players sometimes drag with their own mouse capture and SetWindowPos.
            bool leftDown=PetWindow.ReferenceButtonDown();
            NativePoint screen=new NativePoint();
            bool hasCursor=leftDown && GetPhysicalCursorPos(out screen);
            IntPtr hitWindow=hasCursor && !leftWasDown?WindowFromPhysicalPoint(screen):IntPtr.Zero;
            var cursor=new Point(screen.X,screen.Y);
            ObserveButton(leftDown,hitWindow,cursor,now);
            bool captured=valid && leftDown && info.Capture!=IntPtr.Zero &&
                GetAncestor(info.Capture,2)==Handle && GetForegroundWindow()==Handle;
            if(pointerPressed && (now.IsEmpty || now.Size!=pressBounds.Size)) pointerPressed=false;
            if(!native && !captured)
            {
                gestureStart=Rect.Empty;
                gesture=pointerPressed && hasCursor && Foreground && FollowsPointer(now,cursor)?Gesture.Moving:Gesture.None;
            }
            else
            {
                if(gestureStart.IsEmpty)
                {
                    gestureStart=lastBounds.IsEmpty?now:lastBounds;
                    gesture=Gesture.Pending;
                }
                // Windows owns both coordinates immediately. Infer the kind from geometry
                // without sending a synchronous hit-test into the foreign move/size loop.
                if(gesture==Gesture.Pending)
                {
                    if(now.Size!=gestureStart.Size) gesture=Gesture.Sizing;
                    else if(now.Location!=gestureStart.Location) gesture=Gesture.Moving;
                }
            }
            lastBounds=now;
            return gesture;
        }

        public void Dispose()
        {
            if(originalTopmost.HasValue && Alive && ((GetWindowLong(Handle,-20)&8)!=0)!=originalTopmost.Value)
                SetWindowPos(Handle,new IntPtr(originalTopmost.Value?-1:-2),0,0,0,0,0x213);
            disposed=true;
        }

        // WPF may be running in the system-DPI context. Scope the native calls to physical desktop pixels.
        internal struct PhysicalCoordinates : IDisposable
        {
            static bool supported=true;
            readonly IntPtr prior;
            public PhysicalCoordinates(bool enable):this(enable?new IntPtr(-4):IntPtr.Zero) { }
            PhysicalCoordinates(IntPtr context)
            {
                prior=IntPtr.Zero;
                if(context!=IntPtr.Zero && supported)
                    try { prior=SetThreadDpiAwarenessContext(context); }
                    catch(EntryPointNotFoundException) { supported=false; }
            }
            // SetWindowPos can rescale the destination when caller and HWND have different
            // awareness modes, even when GetWindowRect gave us physical pixels.
            internal static PhysicalCoordinates ForWindow(IntPtr hwnd)
            {
                if(supported)
                    try { return new PhysicalCoordinates(GetWindowDpiAwarenessContext(hwnd)); }
                    catch(EntryPointNotFoundException) { }
                return new PhysicalCoordinates(true);
            }
            public void Dispose() { if(prior!=IntPtr.Zero) SetThreadDpiAwarenessContext(prior); }
        }

        struct NativeRect { public int Left,Top,Right,Bottom; }
        struct NativePoint { public int X,Y; }
        struct GuiInfo
        {
            public int Size,Flags;
            public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;
            public NativeRect CaretRect;
        }
        delegate bool EnumProc(IntPtr hwnd,IntPtr arg);
        [DllImport("user32")] static extern IntPtr WindowFromPhysicalPoint(NativePoint point);
        [DllImport("user32")] static extern bool EnumWindows(EnumProc proc,IntPtr arg);
        [DllImport("user32")] static extern bool EnumChildWindows(IntPtr hwnd,EnumProc proc,IntPtr arg);
        [DllImport("user32")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32")] static extern bool IsZoomed(IntPtr hwnd);
        [DllImport("user32")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32")] static extern IntPtr GetWindow(IntPtr hwnd,uint cmd);
        [DllImport("user32")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flag);
        [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetWindowLong(IntPtr hwnd,int index);
        [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder b,int size);
        [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd,StringBuilder b,int size);
        [DllImport("user32")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
        [DllImport("user32")] static extern bool ShowWindow(IntPtr hwnd,int cmd);
        [DllImport("user32")] static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
        [DllImport("user32")] static extern bool GetGUIThreadInfo(uint id,ref GuiInfo info);
        [DllImport("user32")] static extern bool GetPhysicalCursorPos(out NativePoint pt);
        [DllImport("user32")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32")] static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
        [DllImport("kernel32")] static extern IntPtr OpenProcess(uint access,bool inherit,uint pid);
        [DllImport("kernel32")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32",CharSet=CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int size);
        [DllImport("kernel32",CharSet=CharSet.Unicode,ExactSpelling=true)] static extern int GetApplicationUserModelId(IntPtr process,ref uint size,StringBuilder id);
        [DllImport("shlwapi",CharSet=CharSet.Unicode)] static extern int AssocQueryString(uint flags,uint key,string ext,string verb,StringBuilder value,ref uint length);
        [DllImport("dwmapi")] static extern int DwmGetWindowAttribute(IntPtr hwnd,uint field,out int value,int size);
    }
}

using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace BlueStar
{
    sealed partial class PetWindow
    {
        readonly Grid grid=new Grid { Background=Brushes.Transparent };
        readonly FileOrbits orbits=new FileOrbits();
        readonly OrbitGesture gesture=new OrbitGesture();
        readonly Brush fileSurface=new SolidColorBrush(Color.FromArgb(1,14,27,37));
        bool fileDragLatched,latchReleased,starMoved,canRecycleHover;
        FileStar exportingStar;
        bool exportingWholeStar;
        const double ReentryGrace=0.65;
        double fileLeftAt=-1;
        Point starPress;

        void UpdateFileSurface(Point cursor)
        {
            // A drag can only use the filler close to the pet. Outside that area, only a
            // pending leave or an unreleased latch needs another native button sample.
            bool near=cursor.X>=-24 && cursor.Y>=-24 && cursor.X<=Width+24 && cursor.Y<=Height+24;
            bool acceptsSurface=near && !pressed && orbits.Grabbed==null &&
                (exportingStar==null || exportingWholeStar);
            bool down=(acceptsSurface || fileLeftAt>=0 || (fileDragLatched && !latchReleased)) && DragButtonDown();
            if(fileDragLatched && !down) latchReleased=true;
            if(fileLeftAt>=0 && (!down || elapsed.Elapsed.TotalSeconds-fileLeftAt>ReentryGrace))
                EndFileHover();
            // Layered windows normally pass transparent pixels through to the window behind them.
            // Fill the gaps between legs only during an incoming drag so a circle stays on one OLE target.
            Brush surface=down && acceptsSurface?fileSurface:Brushes.Transparent;
            if(grid.Background!=surface) grid.Background=surface;
        }

        bool PressFileStar(MouseButtonEventArgs e)
        {
            var p=e.GetPosition(this);
            if(orbits.Sealed && Art.InsideHeart(p)) return false;
            var star=orbits.Hit(p);
            if(star==null && e.StylusDevice==null && mouseDownPixel.HasValue)
            {
                p=PointFromScreen(mouseDownPixel.Value);
                if(!orbits.Sealed || !Art.InsideHeart(p)) star=orbits.Hit(p);
            }
            if(star==null) return false;
            mouseDownPixel=null; starMoved=false; starPress=p;
            orbits.Grabbed=star; orbits.GrabPoint=p;
            CaptureMouse(); e.Handled=true; return true;
        }
        bool ReleaseFileStar(MouseButtonEventArgs e)
        {
            var star=orbits.Grabbed;
            if(star==null) return false;
            Point point=e.GetPosition(this);
            bool dragged=starMoved || (point-starPress).Length>=SystemParameters.MinimumHorizontalDragDistance;
            Point anchor=point;
            bool pin=dragged && ReferenceTarget(star.Paths,point,out anchor);
            orbits.Grabbed=null; ReleaseMouseCapture(); e.Handled=true;
            if(!pin) ClearReferencePreview();
            if(pin) OpenReference(star,anchor);
            else if(dragged && !orbits.Sealed && !handlingDrop && !binCommand && Art.InsideHeart(point)) BeginRecycle(star.Paths,star);
            else
            {
                EndFileHover();
                if(!dragged) ShowStarFiles(star,this);
            }
            return true;
        }
        Popup starFiles;
        void CloseStarFiles() { if(starFiles!=null) starFiles.IsOpen=false; }
        internal void ShowStarFiles(FileStar star,UIElement anchor)
        {
            CloseStarFiles();
            var title=new TextBlock { Text="暂存文件 · "+star.Paths.Length+" 项",FontSize=14,Foreground=star.Color,
                FontWeight=FontWeights.Bold,Margin=new Thickness(8,4,8,5) };
            var files=new ListBox { MaxHeight=220,
                Background=Brushes.Transparent,Foreground=Brushes.AliceBlue,BorderThickness=new Thickness(0),
                Padding=new Thickness(4,0,4,4) };
            foreach(string path in star.Paths) files.Items.Add(new ListBoxItem {Content=Path.GetFileName(path),
                Tag=path,ToolTip=path,Foreground=Brushes.AliceBlue,Cursor=Cursors.Hand});
            var stack=new StackPanel(); stack.Children.Add(title); stack.Children.Add(files);
            var popup=new Popup { PlacementTarget=anchor,Placement=PlacementMode.MousePoint,StaysOpen=false,
                AllowsTransparency=true,Child=new Border { Background=new SolidColorBrush(Color.FromRgb(17,25,34)),
                    BorderBrush=star.Color,BorderThickness=new Thickness(1),MinWidth=145,MaxWidth=390,Child=stack } };
            var release=new Button { Content="释放",Foreground=star.Color,HorizontalAlignment=HorizontalAlignment.Right,
                MinWidth=56,Margin=new Thickness(8,2,8,7),ToolTip="取消整批暂存，文件保留在原处" };
            release.Click+=delegate { orbits.Remove(star); popup.IsOpen=false; };
            stack.Children.Add(release);
            string selected=null; Point down=new Point();
            files.PreviewMouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {
                var row=ItemsControl.ContainerFromElement(files,e.OriginalSource as DependencyObject) as ListBoxItem;
                if(row==null) return;
                selected=(string)row.Tag; down=files.PointToScreen(e.GetPosition(files));
                row.IsSelected=true; files.CaptureMouse(); e.Handled=true;
            };
            files.PreviewMouseLeftButtonUp+=delegate {
                selected=null; if(files.IsMouseCaptured) files.ReleaseMouseCapture();
            };
            files.PreviewMouseMove+=delegate(object sender,MouseEventArgs e) {
                if(selected==null || e.LeftButton!=MouseButtonState.Pressed) return;
                var delta=files.PointFromScreen(down)-e.GetPosition(files);
                if(delta.Length<SystemParameters.MinimumHorizontalDragDistance) return;
                string path=selected; selected=null; e.Handled=true;
                if(files.IsMouseCaptured) files.ReleaseMouseCapture();
                popup.IsOpen=false;
                ExportFiles(star,new[]{path},anchor,star.Paths.Length==1);
            };
            popup.Closed+=delegate { if(starFiles==popup) starFiles=null; };
            starFiles=popup; popup.IsOpen=true;
        }
        void MoveFileStar(object sender,MouseEventArgs e)
        {
            var star=orbits.Grabbed;
            if(star==null || e.LeftButton!=MouseButtonState.Pressed) return;
            var p=e.GetPosition(this); orbits.GrabPoint=p;
            if((p-starPress).Length<SystemParameters.MinimumHorizontalDragDistance && !starMoved) return;
            starMoved=true; e.Handled=true;
            Point anchor;
            if(ReferenceTarget(star.Paths,p,out anchor)) { PreviewReference(anchor,p); return; }
            ClearReferencePreview();
            if(!HasReference && star.Paths.Length==1 && PinnedViewer.IsMedia(star.Paths[0]) &&
                new Rect(0,0,ActualWidth,ActualHeight).Contains(p)) return;
            if((p-Art.Heart).Length<Math.Max((starPress-Art.Heart).Length+14,ActualWidth*0.43)) return;
            orbits.Grabbed=null; ReleaseMouseCapture();
            EndFileHover();
            ExportFiles(star,star.Paths,this);
        }
        internal void ExportFiles(FileStar star,string[] selection,UIElement source,bool wholeStar=true)
        {
            string[] available=selection.Where(path=>File.Exists(path) || Directory.Exists(path)).ToArray();
            if(available.Length==0) { Say("原位置已找不到这批文件",4); return; }
            var paths=new StringCollection(); paths.AddRange(available);
            var data=new DataObject(); data.SetFileDropList(paths);
            data.SetData("Preferred DropEffect",new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));
            exportingStar=star; exportingWholeStar=wholeStar;
            try
            {
                var result=DragDrop.DoDragDrop(source,data,DragDropEffects.Move);
                if(result==DragDropEffects.Move)
                {
                    // A destination can skip individual items during a batch move.
                    var moved=available.Where(path=>!File.Exists(path) && !Directory.Exists(path)).ToArray();
                    star.Delivered(moved);
                    if(star.Paths.Length==0) orbits.Remove(star);
                    else if(moved.Length<selection.Length)
                        Say("已移动 "+moved.Length+" 项，轨道上保留 "+star.Paths.Length+" 项",4);
                }
            }
            catch(COMException) { Say("交付未完成，文件仍在轨道上",4); }
            finally { exportingStar=null; exportingWholeStar=false; ClearReferencePreview(); if(draggingFiles || fileLeftAt>=0) EndFileHover(); }
        }

        // INPUT's union is aligned and sized like MOUSEINPUT, its largest member.
        [StructLayout(LayoutKind.Sequential)]
        struct KeyboardInput { public ushort key,scan; public uint flags,time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct MouseInput { public int x,y; public uint data,flags,time; public UIntPtr extra; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputData
        {
            [FieldOffset(0)] public KeyboardInput keyboard;
            [FieldOffset(0)] public MouseInput mouse;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct Input { public uint type; public InputData data; }
        [DllImport("user32.dll")]
        static extern uint SendInput(uint count,Input[] inputs,int size);
        static async void CancelShellDrag()
        {
            var key=new Input { type=1 };
            key.data.keyboard.key=0x1B;
            int size=Marshal.SizeOf(typeof(Input));
            if(SendInput(1,new[]{key},size)!=1) return;
            // Explorer samples Escape in its own OLE loop after our DragOver call returns.
            try { await Task.Delay(90).ConfigureAwait(false); }
            finally { key.data.keyboard.flags=2; SendInput(1,new[]{key},size); }
        }
    }
}

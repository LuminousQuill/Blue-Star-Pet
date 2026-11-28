using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BlueStar
{
    // The two surfaces draw the same pet-relative path up to their shared window edge.
    // Empty space around the stars stays transparent to other desktop windows.
    sealed class OrbitShelf : Window
    {
        readonly PetWindow pet;
        readonly FileOrbits orbits;
        readonly ArcSurface surface;
        readonly StackPanel pager;
        readonly TextBlock pageNumber;
        readonly Button prev,next;
        FileStar pressed;
        Point down;
        Rect lastPet;
        int lastPage=-1,lastPages=-1;

        public OrbitShelf(PetWindow owner,FileOrbits stars)
        {
            pet=owner; orbits=stars;
            Title="收束星轨"; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize;
            WindowStartupLocation=WindowStartupLocation.Manual;
            AllowsTransparency=true; Background=Brushes.Transparent; ShowInTaskbar=false; ShowActivated=false;
            Width=FileOrbits.ShelfWidth;
            surface=new ArcSurface(stars) { Cursor=Cursors.Hand };
            var grid=new Grid(); grid.Children.Add(surface);
            pageNumber=new TextBlock {Foreground=Brushes.AliceBlue,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,8,0)};
            prev=PageButton("‹",-1); next=PageButton("›",1);
            pager=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,
                VerticalAlignment=VerticalAlignment.Bottom, Background=new SolidColorBrush(Color.FromRgb(16,27,36)),
                Visibility=Visibility.Collapsed };
            pager.Children.Add(prev); pager.Children.Add(pageNumber); pager.Children.Add(next); grid.Children.Add(pager);
            Content=grid;
            surface.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) {
                if(!orbits.Sealed) return;
                down=e.GetPosition(surface); pressed=orbits.Hit(down+(Vector)orbits.ShelfOrigin);
                if(pressed!=null) { surface.CaptureMouse(); e.Handled=true; }
            };
            surface.MouseMove+=delegate(object sender,MouseEventArgs e) {
                if(pressed==null || e.LeftButton!=MouseButtonState.Pressed) return;
                if((e.GetPosition(surface)-down).Length<SystemParameters.MinimumHorizontalDragDistance) return;
                var star=pressed; pressed=null; surface.ReleaseMouseCapture(); e.Handled=true;
                pet.ExportFiles(star,star.Paths,this);
            };
            surface.MouseLeftButtonUp+=delegate(object sender,MouseButtonEventArgs e) {
                var star=pressed; pressed=null;
                if(star==null) return;
                surface.ReleaseMouseCapture(); e.Handled=true; pet.ShowStarFiles(star,this);
            };
            surface.LostMouseCapture+=delegate { pressed=null; };
            MouseWheel+=delegate(object sender,MouseWheelEventArgs e) {
                if(orbits.Pages>1) { orbits.ChangePage(e.Delta<0?1:-1); e.Handled=true; }
            };
        }
        Button PageButton(string caption,int delta)
        {
            var b=new Button { Content=caption,Width=28,Height=24,Foreground=Brushes.AliceBlue,
                Background=new SolidColorBrush(Color.FromRgb(31,48,58)),BorderThickness=new Thickness(0),Cursor=Cursors.Hand };
            b.Click+=delegate { orbits.ChangePage(delta); }; return b;
        }
        public void UpdatePlacement()
        {
            var bounds=new Rect(pet.Left,pet.Top,pet.Width,pet.Height);
            if(lastPet!=bounds || Height!=orbits.ShelfHeight) ArrangeBeside(pet.ScreenArea());
        }
        public void ArrangeBeside(Rect work)
        {
            lastPet=new Rect(pet.Left,pet.Top,pet.Width,pet.Height);
            double right=work.Right-lastPet.Right,left=lastPet.Left-work.Left;
            orbits.OnRight=right>=Width || right>=left;
            Left=orbits.OnRight?lastPet.Right:lastPet.Left-Width;
            Height=orbits.ShelfHeight;
            Top=Math.Max(work.Top,Math.Min(work.Bottom-Height,pet.Top+pet.Height*0.46-Height/2));
            orbits.ShelfOrigin=new Point(Left-pet.Left,Top-pet.Top);
        }
        public void Refresh()
        {
            pager.Visibility=orbits.Sealed && orbits.Fold>=0.8 && orbits.Pages>1?Visibility.Visible:Visibility.Collapsed;
            if(lastPage!=orbits.Page || lastPages!=orbits.Pages)
            {
                lastPage=orbits.Page; lastPages=orbits.Pages;
                pageNumber.Text=(lastPage+1)+" / "+lastPages;
                prev.IsEnabled=lastPage>0; next.IsEnabled=lastPage+1<lastPages;
            }
            if(orbits.NeedsDraw) surface.InvalidateVisual();
        }
        sealed class ArcSurface : FrameworkElement
        {
            readonly FileOrbits orbits;
            public ArcSurface(FileOrbits value) { orbits=value; }
            protected override void OnRender(DrawingContext dc)
            {
                dc.PushTransform(new TranslateTransform(-orbits.ShelfOrigin.X,-orbits.ShelfOrigin.Y));
                orbits.DrawOrbits(dc,new Rect(orbits.ShelfOrigin,RenderSize)); dc.Pop();
            }
        }
    }
}

using System;
using System.Threading;
using System.Windows;
using System.Windows.Markup;
using System.Reflection;

namespace BlueStar
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool first;
            using (var mutex=new Mutex(true,@"Local\BlueStarDesktopFamiliar",out first))
            {
                if (!first) return;
                var app=new Application { ShutdownMode=ShutdownMode.OnMainWindowClose };
                using (var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("Panel.xaml"))
                    app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Load(s));
                app.Run(new PetWindow());
            }
        }
    }
}

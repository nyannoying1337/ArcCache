using Microsoft.UI.Xaml;
namespace ArcCache;
public partial class App : Application {
    Window? window;
    public App(){InitializeComponent();UnhandledException+=(_,e)=>{try{string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"IntelArcCacheCap","logs");Directory.CreateDirectory(folder);File.AppendAllText(Path.Combine(folder,"app-unhandled.log"),DateTime.UtcNow.ToString("o")+" "+e.Exception+Environment.NewLine);}catch{}bool fatal=e.Exception is OutOfMemoryException||e.Exception is System.Runtime.InteropServices.SEHException;e.Handled=!fatal;if(!fatal&&window is MainWindow main)main.DispatcherQueue.TryEnqueue(()=>main.ShowUnhandledFailure(e.Exception));};}
    protected override void OnLaunched(LaunchActivatedEventArgs args){window=new MainWindow();string[] command=Environment.GetCommandLineArgs();int at=Array.IndexOf(command,"--preview");if(at>=0&&at+1<command.Length){uint id=0;if(at+2<command.Length)uint.TryParse(command[at+2],out id);int watch=0;if(at+3<command.Length)int.TryParse(command[at+3],out watch);((MainWindow)window).StartPreview(command[at+1],id,watch,command.Contains("--preview-driver-update"));}else window.Activate();}
}

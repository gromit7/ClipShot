using Microsoft.UI.Xaml;
namespace ClipShot;
public partial class App : Application
{
    private MainWindow? window;
    private Mutex? singleInstance;
    internal static readonly bool Isolated = TestEnvironment.IsIsolated(Environment.GetCommandLineArgs());
    internal static readonly uint ShowMessage=Native.RegisterWindowMessage(TestEnvironment.MessageName(Isolated,"Show"));
    internal static readonly uint QuitMessage=Native.RegisterWindowMessage(TestEnvironment.MessageName(Isolated,"Quit"));
    internal static readonly uint MicrophoneMessage=Native.RegisterWindowMessage(TestEnvironment.MessageName(Isolated,"Microphone"));
    public App()
    {
        InitializeComponent();
        if(Isolated)
            UnhandledException+=(_,e)=>{var folder=TestEnvironment.DataFolder(true,Environment.GetEnvironmentVariable("CLIPSHOT_TEST_ROOT"));Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"test-error.txt"),e.Exception.ToString());Environment.ExitCode=1;};
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var command=Environment.GetCommandLineArgs();
        if(command.Contains("--quit")){Native.PostMessage((nint)0xffff,QuitMessage,0,0);Exit();return;}
        var isolated = Isolated;
        singleInstance = new Mutex(true, isolated?"Local\\ClipNest.Test.v1":"Local\\ClipNest.Local.v1", out bool first);
        if (!first) { if(command.Contains("--toggle-microphone")){Native.PostMessage((nint)0xffff,MicrophoneMessage,0,0);Exit();return;}if(!command.Contains("--background"))Native.PostMessage((nint)0xffff,ShowMessage,0,0);Exit(); return; }
        window = new MainWindow(isolated);
        if(command.Contains("--toggle-microphone"))Native.PostMessage((nint)0xffff,MicrophoneMessage,0,0);
        if (Environment.GetCommandLineArgs().Contains("--background")) window.AppWindow.Hide();
        else window.ShowNearCursor();
    }
}

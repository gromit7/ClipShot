namespace ClipShot;
public sealed partial class MainWindow
{
    private void RunUpgradeChecks()
    {
        try
        {
            bool created=store.Get("upgrade-marker","")=="";
            if(created)
            {
                store.Add("text","synthetic upgrade history","<b>synthetic</b>","synthetic rtf");
                store.Add("text","synthetic template");var clip=store.All().First(c=>c.Text=="synthetic template");store.Template(clip.Id,true);store.Edit(clip.Id,"synthetic name");
                store.SetMany(new Dictionary<string,string>{{"upgrade-marker","v1"},{"theme","Dark"},{"days","30"},{"templateorder",clip.Id.ToString()}});
            }
            var all=store.All();var template=all.Single(c=>c.Template);
            bool pass=all.Count==2&&all.Any(c=>c.Text=="synthetic upgrade history"&&c.Html=="<b>synthetic</b>"&&c.Rtf=="synthetic rtf")&&template.Name=="synthetic name"&&store.Get("theme","")=="Dark"&&store.Get("days","")=="30"&&store.Get("templateorder","")==template.Id.ToString();
            if(!pass)throw new InvalidOperationException("Synthetic upgrade state changed.");
            File.WriteAllText(Path.Combine(dataFolder,"upgrade-checks.txt"),"PASS "+(created?"created":"preserved")+" history, formats, template, name, order and settings");
        }
        catch(Exception e){Environment.ExitCode=1;File.WriteAllText(Path.Combine(dataFolder,"upgrade-checks.txt"),"FAIL "+e.Message);}
        if(!Environment.GetCommandLineArgs().Contains("--wait-for-upgrade"))DispatcherQueue.TryEnqueue(Quit);
        else if(Environment.GetCommandLineArgs().Contains("--update-health-test"))
        {
            // Тот же renderer и жизненный цикл плашки, что после настоящего
            // обновления, но только с искусственной базой и без системного ввода.
            var noticeTimer=new Microsoft.UI.Xaml.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(700)};
            noticeTimer.Tick+=async(_,_)=>
            {
                noticeTimer.Stop();
                try
                {
                    var notice=CreateInstalledHistoryNotice(false);
                    await Task.Delay(1000);notice.Close();
                    await Task.Delay(1000);
                    File.WriteAllText(Path.Combine(dataFolder,"startup-notice-checks.txt"),"PASS update notice created and closed while background app remains alive");
                }
                catch(Exception e){LifetimeTrace.Write("test.notice-failed",error:e);Environment.ExitCode=1;Quit();}
            };
            noticeTimer.Start();
        }
    }
}

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
    }
}

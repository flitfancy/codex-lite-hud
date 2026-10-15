using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// Reads metadata of existing main/pet handles and exercises target selection.
// Neither window is activated, moved or sent input by this check.
static class TargetSelectionChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();
            var main=new IntPtr(Int64.Parse(args[0]));var pet=new IntPtr(Int64.Parse(args[1]));
            uint mainPid,petPid;Native.GetWindowThreadProcessId(main,out mainPid);Native.GetWindowThreadProcessId(pet,out petPid);
            if(mainPid==0 || mainPid!=petPid || (Native.GetWindowLongPtr(pet,-20).ToInt64()&0x80)==0)throw new Exception("Need main and tool/pet windows from same Codex process");
            using(var hud=new Hud(false,main))
            {
                var field=typeof(Hud).GetField("codex",Private);
                var select=typeof(Hud).GetMethod("ConsiderForeground",Private);
                select.Invoke(hud,new object[]{pet,petPid});
                bool petSelected=(IntPtr)field.GetValue(hud)==pet;
                select.Invoke(hud,new object[]{main,mainPid});
                bool mainRestored=(IntPtr)field.GetValue(hud)==main;
                field.SetValue(hud,IntPtr.Zero);
                typeof(Hud).GetField("lastDiscovery",Private).SetValue(hud,DateTime.MinValue);
                typeof(Hud).GetMethod("DiscoverWindow",Private).Invoke(hud,null);
                bool discoveryIsPet=(IntPtr)field.GetValue(hud)==pet;
                bool discoveryIsMain=(IntPtr)field.GetValue(hud)==main;
                bool pass=!petSelected && mainRestored && discoveryIsMain && !discoveryIsPet;
                File.WriteAllText(args[2],(pass?"PASS":"FAIL")+": pet_selected="+petSelected+", main_click_restores="+mainRestored+", discovery_selects_pet="+discoveryIsPet+", discovery_selects_main="+discoveryIsMain+". Metadata only; no real window movement or activation.");
                Environment.ExitCode=pass?0:1;
            }
        }
        catch(Exception ex){File.WriteAllText(args[2],ex.ToString());Environment.ExitCode=1;}
    }
}

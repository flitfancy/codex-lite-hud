using System;
using System.IO;
static class AppPathsChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static void Main(string[] args)
    {
        try
        {
            string root=Path.Combine(Path.GetDirectoryName(args[0]),"paths-fixture");Directory.CreateDirectory(root);
            Require(AppPaths.ResolveHome(root)==Path.GetFullPath(root),"Custom CODEX_HOME must resolve");
            Require(AppPaths.FindStateDatabase(root)==null,"Absent database must remain absent");
            foreach(var name in new[]{"state_2.sqlite","state_5.sqlite","state_10.sqlite","state_999.sqlite-wal"})File.WriteAllText(Path.Combine(root,name),"");
            Require(Path.GetFileName(AppPaths.FindStateDatabase(root))=="state_10.sqlite","Database versions must sort numerically and exclude WAL files");
            Require(Product.Version==args[1] && Product.Name=="Codex Lite HUD","Product metadata differs");
            File.WriteAllText(args[0],"PASS product metadata, custom home, absent database, numeric database discovery and WAL exclusion.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}

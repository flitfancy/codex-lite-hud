using System;
using System.IO;

// Appearance has a separate file so choosing a font cannot reset metric order.
sealed class AppearanceSettings
{
    internal string FontFamily=Typography.Family;
    internal void Load()
    {
        try
        {
            var root=SessionReader.Map(Program.Json.DeserializeObject(File.ReadAllText(Path.Combine(Program.DataFolder,"appearance.json"))));
            string family=SessionReader.Text(SessionReader.At(root,"fontFamily"));
            if(!String.IsNullOrWhiteSpace(family) && family.Length<=128)FontFamily=family.Trim();
        }
        catch{FontFamily=Typography.Family;}
    }
    internal void Save()
    {
        try{File.WriteAllText(Path.Combine(Program.DataFolder,"appearance.json"),Program.Json.Serialize(new{fontFamily=FontFamily}));}catch{ }
    }
}

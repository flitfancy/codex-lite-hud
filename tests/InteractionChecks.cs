using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

static class InteractionChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void CheckEmptySessionTitle(Hud hud)
    {
        typeof(Hud).GetField("data",Private).SetValue(hud,null);
        typeof(Hud).GetField("titleRaw",Private).SetValue(hud,"待识别");
        var title=(Label)typeof(Hud).GetField("sessionTitle",Private).GetValue(hud);
        title.Text="待识别";
        typeof(Hud).GetMethod("ClearReadings",Private).Invoke(hud,null);
        typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(hud,null);
        if(title.Text!="待识别" || (string)typeof(Hud).GetField("titleRaw",Private).GetValue(hud)!="待识别")
            throw new Exception("Clearing or relayout replaced the current empty-session status title");
    }
    static void CheckMetricLifecycle(Hud hud)
    {
        var settings=(DisplaySettings)typeof(Hud).GetField("display",Private).GetValue(hud);
        var rows=(IDictionary)typeof(Hud).GetField("rows",Private).GetValue(hud);
        var data=new Data{sessionTitle="HUD",contextPercent=95,model="fixture-model",accountQuota=new QuotaSnapshot{readAt=DateTimeOffset.UtcNow.ToString("o"),windows=new[]{new QuotaWindow{name="5 小时",windowMinutes=300,remaining=8},new QuotaWindow{name="7 天",windowMinutes=10080,remaining=25}}}};
        typeof(Hud).GetField("data",Private).SetValue(hud,data);
        var layout=typeof(Hud).GetMethod("ApplyLayout",Private);
        foreach(string selected in Metrics.All)
        {
            settings.Hidden.Clear();settings.Hidden.UnionWith(Metrics.All.Where(id=>id!=selected));
            typeof(Hud).GetField("layoutPixels",Private).SetValue(hud,600);layout.Invoke(hud,null);
            var numbers=(List<Label>)typeof(Hud).GetField("numericLabels",Private).GetValue(hud);
            var bodies=(List<Label>)typeof(Hud).GetField("bodyLabels",Private).GetValue(hud);
            int expected=selected=="input" || selected=="output" || selected=="context" || selected=="time" || selected=="remaining"?2:1;
            if(numbers.Count!=expected || bodies.Count!=(selected=="time"?4:3))throw new Exception("Wrong text roles when showing only "+selected);
            var checkedReadings=numbers.ToArray();
            foreach(var label in checkedReadings)label.Text="stale-reading";
            foreach(string id in Metrics.All)
            {
                var row=rows[id];var value=(Label)row.GetType().GetField("value",Private).GetValue(row);
                if(numbers.Contains(value)!=(id==selected))throw new Exception("Hidden reading affected typography: "+id+" when showing "+selected);
                value.Text="stale-reading";
            }
            typeof(Hud).GetField("data",Private).SetValue(hud,null);typeof(Hud).GetMethod("ClearReadings",Private).Invoke(hud,null);
            foreach(string id in Metrics.All)
            {
                var row=rows[id];var value=(Label)row.GetType().GetField("value",Private).GetValue(row);
                if(value.Text=="stale-reading")throw new Exception("Clearing omitted "+id);
            }
            if(checkedReadings.Any(label=>label.Text=="stale-reading"))throw new Exception("Clearing omitted a secondary reading for "+selected);
            typeof(Hud).GetField("data",Private).SetValue(hud,data);
        }
        settings.Hidden.Clear();typeof(Hud).GetField("layoutPixels",Private).SetValue(hud,164);layout.Invoke(hud,null);
        var compactNumbers=(List<Label>)typeof(Hud).GetField("numericLabels",Private).GetValue(hud);
        var compactBodies=(List<Label>)typeof(Hud).GetField("bodyLabels",Private).GetValue(hud);
        if(compactNumbers.Count!=14 || compactBodies.Count!=1)throw new Exception("Compact text roles omit a reading or retain captions/capacity");
        var context=(Label)typeof(Hud).GetField("context",Private).GetValue(hud);
        if(context.ForeColor!=Color.FromArgb(185,94,90))throw new Exception("Relayout erased the high-context warning color");
        settings.Reset();typeof(Hud).GetField("layoutPixels",Private).SetValue(hud,0);layout.Invoke(hud,null);
    }
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            using(var hud=new Hud(true,IntPtr.Zero))
            {
                if(hud.Cursor!=Cursors.Default)throw new Exception("Locked HUD uses a resize cursor instead of the normal pointer");
                foreach(Control control in hud.Controls)
                    if(control.Text!="×" && control.Cursor!=Cursors.Default)throw new Exception("Readout uses a resize cursor: "+control.Text);
                var begin=typeof(Hud).GetMethod("BeginDrag",Private);
                begin.Invoke(hud,new object[]{hud,new MouseEventArgs(MouseButtons.Left,1,0,0,0)});
                if((bool)typeof(Hud).GetField("dragging",Private).GetValue(hud))throw new Exception("Locked HUD starts dragging");
                var settings=(DisplaySettings)typeof(Hud).GetField("display",Private).GetValue(hud);
                if(settings.Count!=Metrics.All.Length-1 || !settings.Hidden.Contains("model"))throw new Exception("First run must hide only the model");
                var visibility=new ToolStripMenuItem();var order=new ToolStripMenuItem();
                typeof(Hud).GetMethod("BuildDisplayMenus",Private).Invoke(hud,new object[]{visibility,order});
                if(visibility.DropDownItems.Count!=Metrics.All.Length+2 || order.DropDownItems.Count!=Metrics.All.Length)throw new Exception("Metric menu omits data");
                ((ToolStripMenuItem)visibility.DropDownItems[0]).PerformClick();
                if(!settings.Hidden.Contains("input"))throw new Exception("Visibility menu did not hide input");
                var restored=new DisplaySettings();restored.Load();
                if(!restored.Hidden.Contains("input"))throw new Exception("Visibility was not saved");
                ((ToolStripMenuItem)((ToolStripMenuItem)order.DropDownItems[1]).DropDownItems[0]).PerformClick();
                if(settings.Order[0]!="output")throw new Exception("Order menu did not move output up");
                restored=new DisplaySettings();restored.Load();if(restored.Order[0]!="output")throw new Exception("Order was not saved");
                settings.Reset();settings.Hidden.UnionWith(Metrics.All.Skip(1));
                typeof(Hud).GetMethod("BuildDisplayMenus",Private).Invoke(hud,new object[]{visibility,order});
                if(visibility.DropDownItems[0].Enabled)throw new Exception("Menu permits hiding the final visible metric");
                ((ToolStripMenuItem)visibility.DropDownItems[visibility.DropDownItems.Count-1]).PerformClick();
                if(settings.Count!=Metrics.All.Length-1 || !settings.Hidden.Contains("model") || !settings.Order.SequenceEqual(Metrics.All))throw new Exception("Restore default visibility/order failed");
                var reading=new Data{input=1000,output=200,lastInput=800,lastOutput=50,cache=800,model="fixture-model",accountQuota=new QuotaSnapshot{readAt=DateTimeOffset.Now.ToString("o"),source="fixture",windows=new[]{new QuotaWindow{name="7 天",remaining=25}}}};
                typeof(Hud).GetField("data",Private).SetValue(hud,reading);
                typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(hud,null);
                var rowMap=(IDictionary)typeof(Hud).GetField("rows",Private).GetValue(hud);
                var requestInput=(Label)typeof(Hud).GetField("requestInput",Private).GetValue(hud);var input=(Label)typeof(Hud).GetField("usage",Private).GetValue(hud);
                var requestOutput=(Label)typeof(Hud).GetField("requestOutput",Private).GetValue(hud);var output=(Label)typeof(Hud).GetField("output",Private).GetValue(hud);
                if(requestInput.Text!="本次800" || input.Text!="对话1K" || requestOutput.Text!="本次50" || output.Text!="对话200" || requestInput.Bottom>input.Top || requestOutput.Bottom>output.Top)throw new Exception("Request and conversation usage were not merged under input/output");
                if(rowMap.Contains("last"))throw new Exception("Standalone last input/output row remains");
                if(typeof(Hud).GetFields(Private).Any(field=>typeof(ToolTip).IsAssignableFrom(field.FieldType)))throw new Exception("HUD still owns hover tooltips");
                if(hud.ContextMenuStrip.ShowItemToolTips || visibility.DropDown.ShowItemToolTips || order.DropDown.ShowItemToolTips)throw new Exception("Menu hover tooltips remain enabled");
                if(settings.Order.IndexOf("counts")!=settings.Order.IndexOf("context")+1 || settings.Order.IndexOf("cache")!=settings.Order.IndexOf("counts")+1 || settings.Order.IndexOf("duration")!=settings.Order.IndexOf("time")-1)throw new Exception("Requested default grouping/order differs");
                CheckMetricLifecycle(hud);
                CheckEmptySessionTitle(hud);
                typeof(Hud).GetField("positionLocked",Private).SetValue(hud,false);
                typeof(Hud).GetMethod("UpdateDragCursor",Private).Invoke(hud,null);
                if(hud.Cursor!=Cursors.SizeAll)throw new Exception("Unlocked HUD does not offer two-axis movement");
                typeof(Hud).GetField("mouseStart",Private).SetValue(hud,new Point(10,20));
                typeof(Hud).GetField("windowStart",Private).SetValue(hud,new Point(100,200));
                var location=(Point)typeof(Hud).GetMethod("DragLocation",Private).Invoke(hud,new object[]{new Point(50,80)});
                if(location!=new Point(140,260))throw new Exception("Drag delta was constrained to one axis");
                typeof(Hud).GetField("freeX",Private).SetValue(hud,125);typeof(Hud).GetField("freeY",Private).SetValue(hud,240);
                typeof(Hud).GetMethod("SavePosition",Private).Invoke(hud,null);
                using(var reloaded=new Hud(true,IntPtr.Zero))
                {
                    if((bool)typeof(Hud).GetField("positionLocked",Private).GetValue(reloaded) || (int)typeof(Hud).GetField("freeX",Private).GetValue(reloaded)!=125 || (int)typeof(Hud).GetField("freeY",Private).GetValue(reloaded)!=240)throw new Exception("Free position did not survive restart");
                    typeof(Hud).GetMethod("SetPositionLocked",Private).Invoke(reloaded,new object[]{true});
                    if(reloaded.Cursor!=Cursors.Default || !(bool)typeof(Hud).GetField("positionLocked",Private).GetValue(reloaded))throw new Exception("Lock action did not restore normal pointer");
                }
                visibility.Dispose();order.Dispose();
            }
            File.WriteAllText(Path.Combine(Program.DataFolder,"display.json"),"{\"order\":[\"input\",\"output\",\"context\",\"time\",\"model\",\"last\",\"cache\",\"duration\"],\"hidden\":[]}");
            var migrated=new DisplaySettings();migrated.Load();
            if(!migrated.Order.SequenceEqual(Metrics.All) || !migrated.Hidden.Contains("model") || migrated.Order.Contains("last"))throw new Exception("Old settings did not adopt new grouping/defaults");
            migrated.Hidden.Remove("model");migrated.Save();var explicitModel=new DisplaySettings();explicitModel.Load();
            if(explicitModel.Hidden.Contains("model"))throw new Exception("Explicit model visibility did not persist");
            File.WriteAllText(Path.Combine(Program.DataFolder,"display.json"),"{\"layoutVersion\":2,\"order\":[\"input\",\"output\",\"context\",\"cache\",\"time\",\"duration\",\"counts\",\"speed\",\"remaining\",\"model\"],\"hidden\":[\"speed\"]}");
            var timeLayout=new DisplaySettings();timeLayout.Load();
            if(!timeLayout.Order.SequenceEqual(Metrics.All) || !timeLayout.Hidden.Contains("speed") || timeLayout.Hidden.Contains("model"))throw new Exception("Time layout migration lost order or explicit visibility");
            // Leave the shared fixture state clean for subsequent layout checks.
            File.Delete(Path.Combine(Program.DataFolder,"position.json"));File.Delete(Path.Combine(Program.DataFolder,"display.json"));
            File.WriteAllText(args[0],"PASS default model hidden, merged request/conversation input/output, requested order, no hover tooltips, settings migration, explicit model persistence, visibility/order menus, last-row guard, all metric text roles/clearing, compact roles, relayout warning color, status title survives clearing/layout, two-axis drag and position persistence.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}

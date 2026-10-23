using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;

// One in-flight session read and one quota read are coordinated on the UI
// thread. Background readers return data and never mutate controls.

sealed partial class Hud
{
    void RefreshIdle()
    {
        RecoverMotion();
        if(InMotion)return;
        SampleTheme();
        if(QuotaLogs.Changed && !quotaReading)RefreshAccountQuota();
        if (!reading && !menu.Visible && DateTime.UtcNow - lastRead >= TimeSpan.FromSeconds(1)) RefreshData();
        if (DateTime.UtcNow - lastStatus > TimeSpan.FromSeconds(30)) WriteStatus(true);
    }

    async void RefreshAccountQuota()
    {
        if(quotaReading || IsDisposed)return;
        quotaReading=true;
        try
        {
            await Task.Run(()=>QuotaLogs.Refresh());
            if(IsDisposed)return;
            var snapshot=QuotaReader.Current();
            if(data==null){RenderRemaining();return;}
            if(data.accountQuota!=snapshot){data.accountQuota=snapshot;RenderRemaining();}
        }
        finally{quotaReading=false;}
    }

    async void RefreshData()
    {
        if (reading || IsDisposed) return;
        reading = true;
        bool changed=false,initial=data==null;
        try
        {
            bool automatic = SessionReader.IsAutomatic();
            IntPtr window = codex;
            var next = await Reader.ReadCurrent(window,automatic);
            if (IsDisposed) return;
            if(InMotion)return;
            if(automatic != SessionReader.IsAutomatic() || (automatic && window != codex) || (!automatic && next.sessionId != SessionReader.SelectedId())) return;
            changed=!next.SameDisplay(data) || lastError!="";
            data=next;lastError="";
            RenderTime();
            if(!changed){RenderRemaining();return;}
            tag.Text = automatic ? "自动" : "固定";
            if(codex!=IntPtr.Zero){UpdateDock();if(!dragging)MoveToDock();}
            sessionTitle.Text = nameLayout.Wrapped;
            sessionTitle.ForeColor = accent;
            RenderReadings();
            context.ForeColor = data.contextPercent.GetValueOrDefault() >= 90 ? Color.FromArgb(185,94,90) : accent;
            Invalidate(true);
        }
        catch (Exception ex)
        {
            RenderRemaining();changed=lastError!=ex.Message;lastError=ex.Message;
            if(!changed)return;
            if(SessionReader.IsAutomatic()) { data = null; ResetSessionDisplay("待识别"); capacity.Text = "未识别"; }
            else if (data == null) { ResetSessionDisplay("等待读数"); capacity.Text = "未记录"; }
            else { sessionTitle.Text = data.sessionTitle + " · 旧读数"; }
            titleRaw=sessionTitle.Text;
            sessionTitle.ForeColor = Color.FromArgb(189, 166, 117);
        }
        finally
        {
            reading=false;lastRead=DateTime.UtcNow;
            if(!IsDisposed && !InMotion && ((initial && changed) || DateTime.UtcNow-lastStatus>TimeSpan.FromSeconds(changed?5:30)))WriteStatus(Visible);
        }
    }

    void WriteStatus(bool shown)
    {
        lastStatus = DateTime.UtcNow;
        try
        {
            var windowEvents = new
            {
                location = tracking != null && tracking.LocationEventsActive,
                moving = tracking != null && tracking.MovingEventsActive,
                visibility = tracking != null && tracking.VisibilityEventsActive,
                foreground = tracking != null && tracking.ForegroundEventsActive
            };
            var status = new
            {
                visible = shown,
                nativeVisible = Native.IsWindowVisible(Handle),
                preview = preview,
                targetWindow = codex.ToInt64(),
                followMode = windowEvents.location ? "native-window-events" : "timer-fallback",
                windowEvents = windowEvents,
                positionEvents = tracking == null ? 0 : tracking.PositionEvents,
                x = Left, y = Top, width = Width, height = Height,
                fontFamily = fontFamily,
                requestedFontFamily = appearance.FontFamily,
                nativeBackdrop = backdrop.Ready,
                backgroundLeft = ColorTranslator.ToHtml(backgroundLeft),
                backgroundRight = ColorTranslator.ToHtml(backgroundRight),
                error = lastError,
                data = data
            };
            File.WriteAllText(Path.Combine(Program.DataFolder, "status.json"), Program.Json.Serialize(status));
        }
        catch { /* Diagnostic file failures must not stop the HUD. */ }
    }
}

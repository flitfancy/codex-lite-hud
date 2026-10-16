using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

enum MetricKind { Single, Conversation, Context, Elapsed, Quota }
enum MetricTone { Ink, Muted, Accent, Context }

// Metric order, names and row shapes have one source. Existing saved IDs stay
// stable; adding a simple metric also gives it the standard label lifecycle.
static class Metrics
{
    internal sealed class Definition
    {
        internal readonly string Id,Name,Caption,Empty,CompactEmpty,Direction;
        internal readonly MetricKind Kind;
        internal readonly bool DefaultHidden;
        internal Definition(string id,string name,MetricKind kind=MetricKind.Single,string caption=null,string empty="—",string compactEmpty="—",string direction="",bool defaultHidden=false)
        {Id=id;Name=name;Kind=kind;Caption=caption??name;Empty=empty;CompactEmpty=compactEmpty;Direction=direction;DefaultHidden=defaultHidden;}
        internal int Height(int quotaWindows)
        {
            if(Kind==MetricKind.Elapsed)return 100;
            if(Kind==MetricKind.Conversation || Kind==MetricKind.Context || (Kind==MetricKind.Quota && quotaWindows>1))return 78;
            return 54;
        }
    }

    internal static readonly Definition[] Definitions={
        new Definition("input","输入",MetricKind.Conversation,caption:"输入 token",direction:"入"),
        new Definition("output","输出",MetricKind.Conversation,caption:"输出 token",direction:"出"),
        new Definition("context","上下文",MetricKind.Context,compactEmpty:"占 —"),
        new Definition("counts","轮次/压缩",empty:"— / —",compactEmpty:"轮—/压—"),
        new Definition("cache","缓存占比"),
        new Definition("duration","请求耗时"),
        new Definition("time","用时/跨度",MetricKind.Elapsed,caption:"用时",compactEmpty:"用 —"),
        new Definition("speed","tok/s",compactEmpty:"速 —"),
        new Definition("remaining","剩余额度",MetricKind.Quota,empty:"额度 —",compactEmpty:"余 —"),
        new Definition("model","模型",defaultHidden:true)
    };
    internal static readonly string[] All=Definitions.Select(x=>x.Id).ToArray();
    internal static readonly string[] DefaultHidden=Definitions.Where(x=>x.DefaultHidden).Select(x=>x.Id).ToArray();
    internal static Definition Find(string id){return Definitions.First(x=>x.Id==id);}
    internal static string Name(string id){var definition=Definitions.FirstOrDefault(x=>x.Id==id);return definition==null?id:definition.Name;}
}

// A slot describes one label's position and presentation role. The same slots
// drive layout, visibility, typography, clearing, theme updates and mouse hooks.
sealed class MetricSlot
{
    internal readonly Label Label;
    internal readonly int Top,Height,MinimumQuotaWindows;
    internal readonly bool Reading,CompactVisible;
    internal readonly MetricTone Tone;
    readonly string normalText,compactText;
    internal MetricSlot(Label label,int top,int height,bool reading,MetricTone tone,string normalText,string compactText=null,bool compactVisible=true,int minimumQuotaWindows=1)
    {
        Label=label;Top=top;Height=height;Reading=reading;Tone=tone;
        this.normalText=normalText;this.compactText=compactText??normalText;
        CompactVisible=reading && compactVisible;MinimumQuotaWindows=minimumQuotaWindows;
    }
    internal bool Shown(bool compact,int quotaWindows){return (!compact || CompactVisible) && quotaWindows>=MinimumQuotaWindows;}
    internal string EmptyText(bool compact){return compact?compactText:normalText;}
}

sealed class MetricRow
{
    // Keep the primary caption/value explicit: they are useful to renderers and
    // tests even for rows whose visual order starts with a secondary reading.
    internal readonly Label caption,value;
    internal readonly List<MetricSlot> Slots=new List<MetricSlot>();
    internal MetricRow(Metrics.Definition definition,Label caption,Label value,Label secondary=null,Label secondaryCaption=null)
    {
        this.caption=caption;this.value=value;
        Slots.Add(new MetricSlot(caption,0,18,false,MetricTone.Muted,definition.Caption));
        if(definition.Kind==MetricKind.Conversation)
        {
            Slots.Add(new MetricSlot(secondary??new Label(),18,24,true,MetricTone.Ink,"本次—","本次"+definition.Direction+"—"));
            Slots.Add(new MetricSlot(value,42,24,true,MetricTone.Ink,"对话—","对话"+definition.Direction+"—"));
            return;
        }
        var tone=definition.Kind==MetricKind.Context?MetricTone.Context:definition.Kind==MetricKind.Quota?MetricTone.Accent:MetricTone.Ink;
        Slots.Add(new MetricSlot(value,18,definition.Kind==MetricKind.Elapsed?26:24,true,tone,definition.Empty,definition.CompactEmpty));
        if(definition.Kind==MetricKind.Context)
            Slots.Add(new MetricSlot(secondary??new Label(),42,24,true,MetricTone.Muted,"— / —",compactVisible:false));
        else if(definition.Kind==MetricKind.Elapsed)
        {
            Slots.Add(new MetricSlot(secondaryCaption??new Label(),48,18,false,MetricTone.Muted,"跨度"));
            Slots.Add(new MetricSlot(secondary??new Label(),66,26,true,MetricTone.Ink,"—","跨 —"));
        }
        else if(definition.Kind==MetricKind.Quota)
            Slots.Add(new MetricSlot(secondary??new Label(),42,24,true,MetricTone.Accent,"",minimumQuotaWindows:2));
    }
}

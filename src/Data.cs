using System;

sealed class Data
{
    public string sessionId, sessionTitle, model, usageAt, readAt, selectionSource;
    public long input, output, cache, lastInput, lastOutput, contextUsed, contextLimit, requestMs;
    public double? contextPercent, requestToks;
    public long? turns;
    public long? runningMs,spanMs;
    public long completedTurns,abortedTurns,activeTurns,compactions;
    public SidebarFrame sidebar;
    public QuotaSnapshot accountQuota;
    internal bool SameDisplay(Data other)
    {
        // Live elapsed values update their own label in RenderTime; they do not
        // invalidate the rest of an otherwise unchanged dashboard.
        if(other==null || sessionId!=other.sessionId || sessionTitle!=other.sessionTitle || model!=other.model || usageAt!=other.usageAt || selectionSource!=other.selectionSource || input!=other.input || output!=other.output || cache!=other.cache || lastInput!=other.lastInput || lastOutput!=other.lastOutput || contextUsed!=other.contextUsed || contextLimit!=other.contextLimit || requestMs!=other.requestMs || contextPercent!=other.contextPercent || requestToks!=other.requestToks)return false;
        if(turns!=other.turns || completedTurns!=other.completedTurns || abortedTurns!=other.abortedTurns || activeTurns!=other.activeTurns || compactions!=other.compactions)return false;
        if(accountQuota==other.accountQuota)return true;
        if(accountQuota==null || other.accountQuota==null || accountQuota.readAt!=other.accountQuota.readAt || accountQuota.source!=other.accountQuota.source || accountQuota.windows.Length!=other.accountQuota.windows.Length)return false;
        for(int i=0;i<accountQuota.windows.Length;i++)
        {
            var a=accountQuota.windows[i];var b=other.accountQuota.windows[i];
            if(a.name!=b.name || a.windowMinutes!=b.windowMinutes || a.remaining!=b.remaining || a.resetAt!=b.resetAt)return false;
        }
        return true;
    }
}

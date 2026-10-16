using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Both readers consume append-only JSONL logs, but choose different read/line
// budgets. File identity and small head/tail checkpoints recover replacements
// and ordinary in-place rewrites without re-reading an unchanged large log.
// Arbitrary edits only in the middle, preserving metadata and checkpoints, are
// outside this append-only contract; detecting those would require a full scan.
sealed class IncrementalLogReader
{
    const int PrefixBytes=1024,CheckpointBytes=256;
    readonly int initialTailBytes,maximumLineBytes;
    readonly bool discardOversizedLines;
    readonly Func<byte[],int,bool> acceptPrefix;
    readonly byte[] buffer;
    byte[] line=new byte[1024],head=new byte[0],tail=new byte[0];
    int count;
    long position;
    bool skipping,initialized;
    FileStamp previous;

    internal IncrementalLogReader(int initialTailBytes,int maximumLineBytes,bool discardOversizedLines,Func<byte[],int,bool> acceptPrefix,byte[] sharedBuffer=null)
    {
        this.initialTailBytes=initialTailBytes;this.maximumLineBytes=maximumLineBytes;
        this.discardOversizedLines=discardOversizedLines;this.acceptPrefix=acceptPrefix;
        if(sharedBuffer!=null && sharedBuffer.Length==0)throw new ArgumentException("A shared read buffer must not be empty","sharedBuffer");
        // Sharing is safe only when the caller serializes every Read using it.
        buffer=sharedBuffer??new byte[8192];
    }

    [StructLayout(LayoutKind.Sequential)] struct FileTime { internal uint low,high; }
    [StructLayout(LayoutKind.Sequential)] struct FileInformation
    {
        internal uint attributes;
        internal FileTime created,accessed,written;
        internal uint volume,sizeHigh,sizeLow,links,indexHigh,indexLow;
    }
    [DllImport("kernel32.dll",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle,out FileInformation information);
    struct FileStamp
    {
        internal long length,created,written;
        internal uint volume,indexHigh,indexLow;
        internal bool hasIdentity;
        static long Ticks(FileTime time) { return ((long)time.high<<32)|time.low; }
        internal static FileStamp Read(FileStream stream,string path)
        {
            FileInformation information;
            if(GetFileInformationByHandle(stream.SafeFileHandle,out information))
                return new FileStamp {length=stream.Length,created=Ticks(information.created),written=Ticks(information.written),volume=information.volume,indexHigh=information.indexHigh,indexLow=information.indexLow,hasIdentity=true};
            var file=new FileInfo(path);
            return new FileStamp {length=stream.Length,created=file.CreationTimeUtc.Ticks,written=file.LastWriteTimeUtc.Ticks};
        }
        internal bool SameFile(FileStamp other)
        {
            return hasIdentity && other.hasIdentity
                ? volume==other.volume && indexHigh==other.indexHigh && indexLow==other.indexLow
                : created==other.created;
        }
    }

    internal void Read(string path,Action reset,Action<string> processLine)
    {
        // Disable FileStream's extra buffering: the reader already batches its
        // own reads, and each checkpoint should cost only its bounded bytes.
        using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete,1))
        {
            var current=FileStamp.Read(stream,path);
            bool sameFile=initialized && previous.SameFile(current);
            if(sameFile && current.length==position && current.written==previous.written)return;
            bool changed=!sameFile || current.length<position ||
                (current.length==position && current.written!=previous.written) || !CheckpointsMatch(stream);
            if(changed)
            {
                position=initialTailBytes==0?0:Math.Max(0,current.length-initialTailBytes);
                ClearLine();skipping=position>0;reset();
            }
            stream.Position=position;
            try
            {
                while(position<current.length)
                {
                    int read=stream.Read(buffer,0,(int)Math.Min(buffer.Length,current.length-position));
                    if(read==0)break;
                    Consume(buffer,read,processLine);
                }
            }
            finally
            {
                int size=(int)Math.Min(position,CheckpointBytes);
                head=Sample(stream,0,size);tail=position<=CheckpointBytes?head:Sample(stream,position-size,size);
                previous=current;initialized=true;
            }
        }
    }
    bool CheckpointsMatch(FileStream stream)
    {
        if(!initialized)return false;
        return Matches(stream,0,head) && (position<=CheckpointBytes || Matches(stream,position-tail.Length,tail));
    }
    static bool Matches(FileStream stream,long at,byte[] expected)
    {
        var actual=Sample(stream,at,expected.Length);
        if(actual.Length!=expected.Length)return false;
        for(int i=0;i<actual.Length;i++)if(actual[i]!=expected[i])return false;
        return true;
    }
    static byte[] Sample(FileStream stream,long at,int size)
    {
        var bytes=new byte[size];stream.Position=at;int count=0;
        while(count<size)
        {
            int read=stream.Read(bytes,count,size-count);if(read==0)break;count+=read;
        }
        if(count!=size)Array.Resize(ref bytes,count);
        return bytes;
    }
    void Consume(byte[] bytes,int length,Action<string> processLine)
    {
        int start=0;
        while(start<length)
        {
            int end=Array.IndexOf(bytes,(byte)10,start,length-start);
            int size=(end<0?length:end)-start;
            // Advance only across bytes handed to the line buffer. If parsing
            // rejects a record, a later read still reaches the following line.
            position+=size+(end<0?0:1);
            if(end<0){Append(bytes,start,size);return;}
            string complete=null;
            try
            {
                Append(bytes,start,size);
                if(!skipping && count>0 && (acceptPrefix==null || acceptPrefix(line,count)))complete=Encoding.UTF8.GetString(line,0,count);
            }
            // Even an oversized line has consumed this newline. Its exception
            // must not leave the next complete record in skipping mode.
            finally {ClearLine();}
            if(complete!=null)processLine(complete);
            start=end+1;
        }
    }
    void Append(byte[] bytes,int start,int length)
    {
        if(skipping)return;
        if(acceptPrefix!=null && count<PrefixBytes)
        {
            int prefix=Math.Min(length,PrefixBytes-count);Copy(bytes,start,prefix);start+=prefix;length-=prefix;
            if(count>=PrefixBytes && !acceptPrefix(line,count)){skipping=true;count=0;return;}
        }
        if(count+length>maximumLineBytes)
        {
            skipping=true;count=0;
            if(!discardOversizedLines)throw new InvalidDataException("会话记录单行过长");
            return;
        }
        Copy(bytes,start,length);
    }
    void Copy(byte[] bytes,int start,int length)
    {
        if(line.Length<count+length)Array.Resize(ref line,Math.Min(maximumLineBytes,Math.Max(line.Length*2,count+length)));
        if(length>0){Buffer.BlockCopy(bytes,start,line,count,length);count+=length;}
    }
    void ClearLine()
    {
        count=0;skipping=false;
        // A rare large tool result must not enlarge every cached session for
        // the rest of the process lifetime.
        if(line.Length>65536)line=new byte[1024];
    }
}

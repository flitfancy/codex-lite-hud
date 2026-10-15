using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

static class Metrics
{
    internal static readonly string[] All={"input","output","context","counts","cache","duration","time","speed","remaining","model"};
    internal static readonly string[] Names={"输入","输出","上下文","轮次/压缩","缓存占比","请求耗时","用时/跨度","tok/s","剩余额度","模型"};
    internal static string Name(string id){int index=Array.IndexOf(All,id);return index<0?id:Names[index];}
    internal static int Height(string id){return id=="time"?100:id=="input" || id=="output"?78:id=="context"?70:54;}
}

sealed class DisplaySettings
{
    internal readonly List<string> Order=new List<string>(Metrics.All);
    internal readonly HashSet<string> Hidden=new HashSet<string>{"model"};
    internal IEnumerable<string> Visible {get{foreach(string id in Order)if(!Hidden.Contains(id))yield return id;}}
    internal int Count {get{int count=0;foreach(string id in Visible)count++;return count;}}
    internal int NaturalHeight {get{int height=88;foreach(string id in Visible)height+=Metrics.Height(id);return height;}}
    internal void Reset(){Order.Clear();Order.AddRange(Metrics.All);Hidden.Clear();Hidden.Add("model");}
    internal void Move(string id,int delta)
    {
        int index=Order.IndexOf(id),next=index+delta;if(index<0 || next<0 || next>=Order.Count)return;
        Order.RemoveAt(index);Order.Insert(next,id);
    }
    internal void Load()
    {
        try
        {
            var root=SessionReader.Map(Program.Json.DeserializeObject(File.ReadAllText(Path.Combine(Program.DataFolder,"display.json"))));
            bool current=SessionReader.Count(SessionReader.At(root,"layoutVersion"))>=2;
            var order=SessionReader.At(root,"order") as IEnumerable;
            if(current && order!=null)
            {
                var valid=new List<string>();
                foreach(object value in order){string id=SessionReader.Text(value);if(Array.IndexOf(Metrics.All,id)>=0 && !valid.Contains(id))valid.Add(id);}
                foreach(string id in Metrics.All)if(!valid.Contains(id))valid.Add(id);
                Order.Clear();Order.AddRange(valid);
            }
            var hidden=SessionReader.At(root,"hidden") as IEnumerable;
            if(current)Hidden.Clear();
            if(hidden!=null)foreach(object value in hidden){string id=SessionReader.Text(value);if(Array.IndexOf(Metrics.All,id)>=0)Hidden.Add(id);}
            if(SessionReader.Count(SessionReader.At(root,"layoutVersion"))<3)
            {
                Order.Remove("counts");Order.Insert(Order.IndexOf("context")+1,"counts");
                Order.Remove("cache");Order.Insert(Order.IndexOf("counts")+1,"cache");
                Order.Remove("duration");Order.Insert(Order.IndexOf("time"),"duration");
            }
            if(Count==0)Hidden.Remove(Order[0]);
        }
        catch{Reset();}
    }
    internal void Save()
    {
        try{File.WriteAllText(Path.Combine(Program.DataFolder,"display.json"),Program.Json.Serialize(new{layoutVersion=3,order=Order.ToArray(),hidden=new List<string>(Hidden).ToArray()}));}catch{ }
    }
}

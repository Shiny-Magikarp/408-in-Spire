using System.Text.RegularExpressions;

namespace Spire408.Core;

// Narrow contradictions with explicit predicates. These do not establish the
// truth of arbitrary prose or replace the classifier's uncertainty handling.
public static class SemanticGuards
{
    public static bool Contradicts(string answer, string reference)
    {
        static string Compact(string value) => Regex.Replace(value, @"\s+", "").Replace('−','-').Replace("节点","结点");
        string a=Compact(answer), r=Compact(reference);
        if (r.Contains("n-1条边") && Regex.IsMatch(a,@"(?:有|具有|是|为|=)n条边[。；，.!;]?$")) return true;
        if (r.Contains("中断") && r.Contains("断点")
            && Regex.IsMatch(a,@"(?:必须|应当|需要|要)从主程序第一条指令重新开始")) return true;
        if (r.Contains("LRU") && (r.Contains("最长时间没有") || r.Contains("最久未"))
            && Regex.IsMatch(a,@"淘汰(?:的总是|的是)?(?:刚刚|最近刚)访问")) return true;
        if (r.Contains("同一进程") && r.Contains("线程") && r.Contains("共享") && r.Contains("地址空间")
            && Regex.IsMatch(a,@"线程.*(?:必须|需要).*与.*进程完全隔离的地址空间")) return true;
        return false;
    }
}

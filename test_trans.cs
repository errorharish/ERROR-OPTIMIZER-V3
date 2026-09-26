using System;
using System.IO;
using BiosOptimizer.Safety;

class Program
{
    static void Main(string[] args)
    {
        var tm = new TransactionManager();
        var trans = tm.GetLatestTransaction("Normal") ?? tm.GetLatestTransaction("Pro") ?? tm.GetLatestTransaction("Ultimate") ?? tm.GetLatestTransaction("MaximumPerformance");
        
        if (trans == null)
        {
            Console.WriteLine("No transaction found!");
            return;
        }
        
        Console.WriteLine($"Found transaction: {trans.TransactionId}");
        Console.WriteLine($"Snapshots count: {trans.Snapshots.Count}");
        
        foreach (var s in trans.Snapshots)
        {
            Console.WriteLine($"  - {s.ActionName} ({s.GetType().Name}) - Restored: {s.Restored}");
        }
        
        Console.WriteLine("HasRestorable: " + tm.HasRestorableTransaction(trans.TierId));
    }
}

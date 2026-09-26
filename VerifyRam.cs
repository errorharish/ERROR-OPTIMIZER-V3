using System;
using BiosOptimizer.Core.ActionHandlers;
using BiosOptimizer.Core.Models;
using BiosOptimizer.Core.Interfaces;

public class Program
{
    public static void Main()
    {
        var handler = new SvchostSplitThresholdHandler();
        
        // Test mapping
        var rams = new[] { 4L, 6L, 8L, 12L, 16L, 24L, 32L, 64L };
        foreach (var gb in rams)
        {
            var bytes = gb * 1024 * 1024 * 1024;
            var val = handler.GetAuthoritativeValueForRam(bytes);
            Console.WriteLine($"RAM: {gb} GB -> 0x{val:X}");
        }
    }
}

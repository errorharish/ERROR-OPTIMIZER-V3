using Xunit;
using BiosOptimizer.Core.ActionHandlers;
using BiosOptimizer.Core.Models;
using System.Collections.Generic;

namespace BiosOptimizer.Core.Tests
{
    public class Phase5ValidationTests
    {
        [Fact]
        public void VerifyAuthoritativeMapping()
        {
            var handler = new SvchostSplitThresholdHandler();
            var expected = new Dictionary<long, uint>
            {
                { 4L * 1024 * 1024 * 1024, 0x4000000 },
                { 6L * 1024 * 1024 * 1024, 0x600000 },
                { 8L * 1024 * 1024 * 1024, 0x800000 },
                { 12L * 1024 * 1024 * 1024, 0xC00000 },
                { 16L * 1024 * 1024 * 1024, 0x1000000 },
                { 24L * 1024 * 1024 * 1024, 0x1800000 },
                { 32L * 1024 * 1024 * 1024, 0x2000000 },
                { 64L * 1024 * 1024 * 1024, 0x4000000 }
            };

            foreach (var kvp in expected)
            {
                var val = handler.GetAuthoritativeValueForRam(kvp.Key);
                Assert.Equal(kvp.Value, val);
            }
        }
    }
}

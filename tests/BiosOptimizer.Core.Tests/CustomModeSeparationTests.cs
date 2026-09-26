using BiosOptimizer.Core.Implementations;
using BiosOptimizer.Core.Interfaces;
using BiosOptimizer.GUI.Services;
using BiosOptimizer.GUI.ViewModels;
using BiosOptimizer.IPC.Contracts;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace BiosOptimizer.Core.Tests
{
    public class MockSeparationIpcClient : IIpcClient
    {
        public bool IsServiceAvailable => ConnectionState == IpcConnectionState.Connected;
        public IpcConnectionState ConnectionState { get; set; } = IpcConnectionState.Connected;
        public string ServiceBuildId => "2026.08.22.1429";
        public event Action<bool>? ServiceAvailabilityChanged { add { } remove { } }
        public event Action<IpcConnectionState>? ConnectionStateChanged { add { } remove { } }

        public void SetConnectionState(IpcConnectionState state)
        {
            ConnectionState = state;
        }

        public string LastPayloadSent { get; set; } = string.Empty;

        public Task<bool> WaitForReadyAsync(TimeSpan timeout, CancellationToken ct = default)
        {
            return Task.FromResult(true);
        }

        public Task<IpcResponse> SendRequestAsync(IpcMessageType messageType, string? payload = null, CancellationToken ct = default)
        {
            if (messageType == IpcMessageType.ApplyInputOptimization)
            {
                LastPayloadSent = payload ?? string.Empty;
                return Task.FromResult(new IpcResponse { Success = true });
            }
            if (messageType == IpcMessageType.PlanInputOptimization)
            {
                return Task.FromResult(new IpcResponse { Success = true, Data = "{\"actions\":[]}" });
            }
            return Task.FromResult(new IpcResponse { Success = true });
        }

        public async IAsyncEnumerable<IpcResponse> SendStreamingRequestAsync(IpcMessageType messageType, string? payload = null, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.Yield();
            yield return new IpcResponse { Success = true, Data = "{}" };
        }
    }

    public class CustomModeSeparationTests
    {
        [Fact]
        public void NormalAndAdvancedSelection_AreCompletelyIsolated()
        {
            var ipc = new MockSeparationIpcClient();
            var vm = new InputOptimizerViewModel(ipc);

            // Add dummy Normal items
            var n1 = new InputOptimizationItemModel { Id = "normal.1", Name = "MouseSpeed", Category = "Mouse", Risk = "CORE", Applicable = true, Status = "PENDING" };
            var n2 = new InputOptimizationItemModel { Id = "normal.2", Name = "MouseSens", Category = "Mouse", Risk = "CORE", Applicable = true, Status = "PENDING" };
            vm.NormalApplicableItems.Add(n1);
            vm.NormalApplicableItems.Add(n2);

            // Add dummy Advanced items
            var a1 = new InputOptimizationItemModel { Id = "adv.1", Name = "MouseQueue", Category = "Queue", Risk = "ADVANCED", Applicable = true, Status = "PENDING" };
            var a2 = new InputOptimizationItemModel { Id = "adv.2", Name = "KeyboardQueue", Category = "Queue", Risk = "ADVANCED", Applicable = true, Status = "PENDING" };
            vm.AdvancedApplicableItems.Add(a1);
            vm.AdvancedApplicableItems.Add(a2);

            // 1. Initial State: 0 selected
            Assert.Equal(0, vm.NormalSelectedCount);
            Assert.Equal(0, vm.AdvancedSelectedCount);

            // 2. Select All Normal
            vm.NormalSelectAllCommand.Execute(null);
            Assert.Equal(2, vm.NormalSelectedCount);
            Assert.Equal(0, vm.AdvancedSelectedCount);

            // 3. Select All Advanced
            vm.AdvancedSelectAllCommand.Execute(null);
            Assert.Equal(2, vm.NormalSelectedCount);
            Assert.Equal(2, vm.AdvancedSelectedCount);

            // 4. Deselect All Normal -> Advanced must remain 2
            vm.NormalDeselectAllCommand.Execute(null);
            Assert.Equal(0, vm.NormalSelectedCount);
            Assert.Equal(2, vm.AdvancedSelectedCount);

            // 5. Deselect All Advanced -> Normal must remain 0
            vm.AdvancedDeselectAllCommand.Execute(null);
            Assert.Equal(0, vm.NormalSelectedCount);
            Assert.Equal(0, vm.AdvancedSelectedCount);
        }

        [Fact]
        public async Task NormalOptimization_ButtonAndPayload_AreIsolated()
        {
            var ipc = new MockSeparationIpcClient();
            var vm = new InputOptimizerViewModel(ipc);

            var n1 = new InputOptimizationItemModel { Id = "normal.1", Name = "MouseSpeed", Category = "Mouse", Risk = "CORE", Applicable = true, Status = "PENDING", IsSelected = true };
            var a1 = new InputOptimizationItemModel { Id = "adv.1", Name = "MouseQueue", Category = "Queue", Risk = "ADVANCED", Applicable = true, Status = "PENDING", IsSelected = true };
            vm.NormalApplicableItems.Add(n1);
            vm.AdvancedApplicableItems.Add(a1);

            // Execute Normal Custom
            vm.OptimizeNormalCustomCommand.Execute(null);
            await Task.Delay(600);

            // Must contain ONLY normal.1 and NOT adv.1
            Assert.Contains("normal.1", ipc.LastPayloadSent);
            Assert.DoesNotContain("adv.1", ipc.LastPayloadSent);
            Assert.Equal("OPTIMIZING NORMAL INPUT", vm.ProgressTitle);
        }

        [Fact]
        public async Task AdvancedOptimization_ButtonAndPayload_AreIsolated()
        {
            var ipc = new MockSeparationIpcClient();
            var vm = new InputOptimizerViewModel(ipc);

            var n1 = new InputOptimizationItemModel { Id = "normal.1", Name = "MouseSpeed", Category = "Mouse", Risk = "CORE", Applicable = true, Status = "PENDING", IsSelected = true };
            var a1 = new InputOptimizationItemModel { Id = "adv.1", Name = "MouseQueue", Category = "Queue", Risk = "ADVANCED", Applicable = true, Status = "PENDING", IsSelected = true };
            vm.NormalApplicableItems.Add(n1);
            vm.AdvancedApplicableItems.Add(a1);

            // Execute Advanced Custom
            vm.OptimizeAdvancedCustomCommand.Execute(null);
            await Task.Delay(600);

            // Must contain ONLY adv.1 and NOT normal.1
            Assert.Contains("adv.1", ipc.LastPayloadSent);
            Assert.DoesNotContain("normal.1", ipc.LastPayloadSent);
            Assert.Equal("OPTIMIZING ADVANCED INPUT", vm.ProgressTitle);
        }
    }
}

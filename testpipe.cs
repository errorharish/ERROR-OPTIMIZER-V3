using System;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;

class Program {
    static void Main() {
        try {
            var pipeSecurity = new PipeSecurity();
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                PipeAccessRights.ReadWrite,
                AccessControlType.Allow));
            
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
                
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            var pipe = NamedPipeServerStreamAcl.Create(
                "TestPipe123", PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Message, PipeOptions.Asynchronous,
                0, 0, pipeSecurity);
                
            Console.WriteLine("Success");
        } catch(Exception e) {
            Console.WriteLine("Error: " + e.ToString());
        }
    }
}

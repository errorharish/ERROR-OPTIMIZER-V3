using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        Console.WriteLine("Connecting to IPC...");
        using var client = new NamedPipeClientStream(".", "BiosOptimizerIpc", PipeDirection.InOut, PipeOptions.Asynchronous);
        try {
            await client.ConnectAsync(5000);
            using var reader = new StreamReader(client);
            using var writer = new StreamWriter(client) { AutoFlush = true };

            Console.WriteLine("Sending PreviewTier: Normal");
            var req = JsonSerializer.Serialize(new { Type = "PreviewTier", Payload = "Normal" });
            await writer.WriteLineAsync(req);
            
            var res = await reader.ReadLineAsync();
            Console.WriteLine("Preview Response:");
            Console.WriteLine(res);
            
        } catch (Exception ex) {
            Console.WriteLine(ex.Message);
        }
    }
}

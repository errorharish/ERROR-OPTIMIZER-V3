using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Connecting to named pipe...");
        using var pipe = new NamedPipeClientStream(".", "BiosOptimizerPipe", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(5000);
        Console.WriteLine("Connected!");

        using var reader = new StreamReader(pipe);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };

        var req = new { Type = 5, Payload = "Normal" }; // 5 = RestoreTier
        var jsonReq = JsonSerializer.Serialize(req);
        
        Console.WriteLine($"Sending: {jsonReq}");
        await writer.WriteLineAsync(jsonReq);
        
        Console.WriteLine("Reading stream...");
        while (true)
        {
            string? line = await reader.ReadLineAsync();
            if (line == null) break;
            
            Console.WriteLine(line);
            
            try
            {
                var resp = JsonSerializer.Deserialize<JsonElement>(line);
                if (resp.TryGetProperty("Success", out var s) && s.GetBoolean() == false)
                {
                    Console.WriteLine("Got error response, exiting.");
                    break;
                }
                
                if (resp.TryGetProperty("Data", out var dataProp))
                {
                    var dataStr = dataProp.GetString();
                    if (!string.IsNullOrEmpty(dataStr))
                    {
                        var dataDoc = JsonSerializer.Deserialize<JsonElement>(dataStr);
                        if (dataDoc.TryGetProperty("FinalResult", out var f) && f.GetBoolean() == true)
                        {
                            Console.WriteLine("Got FinalResult, exiting.");
                            break;
                        }
                    }
                }
            }
            catch {}
        }
    }
}

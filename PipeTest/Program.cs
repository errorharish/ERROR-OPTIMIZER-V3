using System;
using System.IO.Pipes;
using System.Text;
using System.Threading.Tasks;

class Program {
    static async Task Main() {
        try {
            using var pipe = new NamedPipeClientStream(".", "BiosOptimizer", PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync(2000);
            string msg = "{\"Type\":10,\"Payload\":\"Normal\"}"; 
            var bytes = Encoding.UTF8.GetBytes(msg);
            await pipe.WriteAsync(bytes, 0, bytes.Length);
            
            var buffer = new byte[8192];
            var read = await pipe.ReadAsync(buffer, 0, buffer.Length);
            Console.WriteLine(Encoding.UTF8.GetString(buffer, 0, read));
        } catch (Exception e) {
            Console.WriteLine("Error: " + e.Message);
        }
    }
}

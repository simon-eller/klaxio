using System;
using System.Text;
using System.Threading.Tasks;

namespace Klaxio
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(L.Get("AppTitle"));
            Console.WriteLine();

            var game   = new GameHub();
            var server = new WebSocketServer(game);
            game.WebSocketServer = server;

            game.InitButtons();
            await server.StartAsync("http://localhost:8765/");
        }
    }
}

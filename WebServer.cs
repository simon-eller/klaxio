using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Klaxio
{
    /// <summary>
    /// One HttpListener serving both the embedded single-page frontend and the
    /// WebSocket the browsers talk to.
    /// </summary>
    class WebSocketServer
    {
        readonly GameHub         _game;
        readonly PlaylistCatalog _catalog    = new PlaylistCatalog();
        readonly List<WebSocket> _clients    = new List<WebSocket>();
        readonly object          _clientLock = new object();

        // The HTML shell is embedded as a resource and read once.
        static readonly string _html = LoadHtml();

        public WebSocketServer(GameHub game) => _game = game;

        static string LoadHtml()
        {
            var asm = Assembly.GetExecutingAssembly();
            // Resource name: <RootNamespace>.<filename>
            using var stream = asm.GetManifestResourceStream("Klaxio.frontend.html");
            if (stream == null)
                throw new Exception("Embedded resource 'Klaxio.frontend.html' not found. " +
                                    "Make sure frontend.html is in the project with Build Action = EmbeddedResource.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        public async Task StartAsync(string url)
        {
            var listener = new HttpListener();
            string httpUrl = url.Replace("ws://", "http://");
            if (!httpUrl.EndsWith("/")) httpUrl += "/";
            listener.Prefixes.Add(httpUrl);
            listener.Start();

            Console.WriteLine(L.Get("WsListening", url));
            Console.WriteLine(L.Get("Hotkeys"));
            Console.WriteLine();

            // Open browser automatically
            Console.WriteLine(L.Get("BrowserOpening"));
            try { Process.Start(new ProcessStartInfo(httpUrl) { UseShellExecute = true }); }
            catch { /* ignore if no default browser */ }

            _ = Task.Run(ConsoleInputLoop);

            // Cover art comes from YouTube and takes a moment. Fetching it now
            // means the settings screen finds the catalogue ready.
            _ = _catalog.GetJsonAsync();

            while (true)
            {
                var ctx = await listener.GetContextAsync();

                if (ctx.Request.IsWebSocketRequest)
                    _ = Task.Run(() => HandleClientAsync(ctx));
                else
                    _ = ServeHttp(ctx);
            }
        }

        /// <summary>Serves the playlist catalogue and wwwroot assets; every other path falls back to the SPA shell.</summary>
        async Task ServeHttp(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath.TrimStart('/');

                if (path == "api/playlists")
                {
                    var json = await _catalog.GetJsonAsync();
                    var body = Encoding.UTF8.GetBytes(json);
                    ctx.Response.ContentType     = "application/json; charset=utf-8";
                    ctx.Response.ContentLength64 = body.Length;
                    await ctx.Response.OutputStream.WriteAsync(body, 0, body.Length);
                    ctx.Response.Close();
                    return;
                }

                if (path.StartsWith("wwwroot/") || path.StartsWith("css/") || path.StartsWith("fonts/")
                    || path.StartsWith("img/") || path.StartsWith("js/"))
                {
                    var resourceName = $"Klaxio.wwwroot.{path.Replace('/', '.')}";
                    var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        using (stream)
                        {
                            ctx.Response.ContentType     = GetMimeType(path);
                            ctx.Response.ContentLength64 = stream.Length;
                            stream.CopyTo(ctx.Response.OutputStream);
                        }
                        ctx.Response.Close();
                        return;
                    }

                    ctx.Response.StatusCode = 404;
                    ctx.Response.Close();
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(_html);
                ctx.Response.ContentType     = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
            catch { }
        }

        static string GetMimeType(string path) => Path.GetExtension(path) switch
        {
            ".html"  => "text/html; charset=utf-8",
            ".css"   => "text/css",
            ".js"    => "application/javascript; charset=utf-8",
            ".woff2" => "font/woff2",
            ".woff"  => "font/woff",
            ".ttf"   => "font/ttf",
            ".svg"   => "image/svg+xml",
            ".ico"   => "image/x-icon",
            ".png"   => "image/png",
            ".json"  => "application/json",
            _        => "application/octet-stream"
        };

        async Task HandleClientAsync(HttpListenerContext ctx)
        {
            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            var ws    = wsCtx.WebSocket;

            Console.WriteLine(L.Get("WsClientConn", ctx.Request.RemoteEndPoint));
            lock (_clientLock) _clients.Add(ws);

            await SendAsync(ws, _game.GetInitState());

            var buffer = new byte[8192];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close) break;

                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    try
                    {
                        var doc = JsonDocument.Parse(text);
                        if (doc.RootElement.TryGetProperty("cmd", out var cmdEl))
                        {
                            JsonElement? payload = doc.RootElement.ValueKind == JsonValueKind.Object
                                ? doc.RootElement : (JsonElement?)null;
                            var response = _game.ProcessCommand(cmdEl.GetString(), payload);
                            if (response != null) await BroadcastAsync(response);
                        }
                    }
                    catch { /* malformed JSON - ignore */ }
                }
            }
            catch { }
            finally
            {
                lock (_clientLock) _clients.Remove(ws);
                Console.WriteLine(L.Get("WsClientDisconn", ctx.Request.RemoteEndPoint));
                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            }
        }

        public async Task BroadcastAsync(object message)
        {
            var json    = JsonSerializer.Serialize(message);
            var bytes   = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            List<WebSocket> snapshot;
            lock (_clientLock) snapshot = new List<WebSocket>(_clients);

            foreach (var ws in snapshot)
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                        await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                }
                catch { }
            }
        }

        async Task SendAsync(WebSocket ws, object message)
        {
            var json  = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        void ConsoleInputLoop()
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true).Key;

                if (key == ConsoleKey.Q)
                {
                    Console.WriteLine(L.Get("Quitting"));
                    Environment.Exit(0);
                }

                var cmd = _game.HotkeyCommand(key);
                if (cmd != null)
                {
                    var response = _game.ProcessCommand(cmd);
                    if (response != null) BroadcastAsync(response).GetAwaiter().GetResult();
                }
            }
        }
    }
}

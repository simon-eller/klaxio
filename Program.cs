using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using EchoButtons;

namespace Klaxio
{
    // l11n
    static class L
    {
        static ResourceManager _rm = new ResourceManager(
            "Klaxio.Strings", Assembly.GetExecutingAssembly());

        public static CultureInfo Culture { get; private set; } = CultureInfo.InvariantCulture; // = English

        public static void SetLanguage(string lang)
        {
            Culture = lang == "de"
                ? new CultureInfo("de")
                : CultureInfo.InvariantCulture;
        }

        public static string Get(string key) =>
            _rm.GetString(key, Culture) ?? $"[{key}]";

        public static string Get(string key, params object[] args) =>
            string.Format(Get(key), args);

        // All UI strings as a dictionary for the browser
        public static Dictionary<string, string> UiStrings() => new Dictionary<string, string>
        {
            ["player1"]         = Get("Player1"),
            ["player2"]         = Get("Player2"),
            ["phaseReady"]      = Get("PhaseReady"),
            ["phaseArmed"]      = Get("PhaseArmed"),
            ["phaseBuzzed"]     = Get("PhaseBuzzed"),
            ["phaseCorrect"]    = Get("PhaseCorrect"),
            ["phaseWrong"]      = Get("PhaseWrong"),
            ["btnArm"]          = Get("BtnArm"),
            ["btnCorrect"]      = Get("BtnCorrect"),
            ["btnWrong"]        = Get("BtnWrong"),
            ["btnReset"]        = Get("BtnReset"),
            ["btnResetScores"]  = Get("BtnResetScores"),
            ["toastBuzzed"]     = Get("ToastBuzzed"),
            ["confirmReset"]    = Get("ConfirmReset"),
            ["connConnecting"]  = Get("ConnConnecting"),
            ["connConnected"]   = Get("ConnConnected"),
            ["connReconnecting"]= Get("ConnReconnecting"),
            ["toastConnected"]  = Get("ToastConnected"),
            ["langToggle"]      = Get("LangToggle"),
        };
    }

    // Quiz logic
    enum Phase { Waiting, Armed, Buzzed, Correct, Wrong }

    class QuizGame
    {
        public Phase Phase { get; private set; } = Phase.Waiting;
        public int[] Scores { get; } = { 0, 0 };
        public string[] Names { get; } = { L.Get("Player1"), L.Get("Player2") };
        public int Winner { get; private set; } = -1;

        public WebSocketServer WebSocketServer { get; set; }

        EchoButton _echoButton;
        List<string> _buttonOrder = new List<string>();
        object _lock = new object();

        public void InitButtons()
        {
            _echoButton = new EchoButton();

            _echoButton.FoundPairedDevice += (s, e) =>
            {
                lock (_lock)
                {
                    int idx = _buttonOrder.Count;
                    _buttonOrder.Add(e.ButtonName);
                    if (idx < 2) Names[idx] = $"{L.Get(idx == 0 ? "Player1" : "Player2")}  [{e.ButtonName}]";
                }
                Console.WriteLine(L.Get("BtFound", e.ButtonName));
            };

            _echoButton.NoPairedDevices += (s, e) =>
                Console.WriteLine(L.Get("BtNone"));

            _echoButton.Connected += (s, e) =>
                Console.WriteLine(L.Get("BtConnected", e.ButtonName));

            _echoButton.Disconnected += (s, e) =>
                Console.WriteLine(L.Get("BtDisconnected", e.ButtonName));

            _echoButton.Pressed += OnButtonPressed;
            _echoButton.Released += (s, e) => { };

            _echoButton.StartListening();
            Console.WriteLine(L.Get("BtSearching"));
        }

        void OnButtonPressed(object sender, ButtonEventArgs e)
        {
            int playerIdx = GetPlayerIndex(e.ButtonName);
            Console.WriteLine(L.Get("BtnPressed", e.ButtonName, playerIdx + 1));

            lock (_lock)
            {
                if (Phase != Phase.Armed) return;
                Winner = playerIdx;
                Phase = Phase.Buzzed;
            }

            WebSocketServer?.BroadcastAsync(new
            {
                @event = "buzzed",
                winner = playerIdx,
                name = Names[playerIdx]
            }).GetAwaiter().GetResult();
        }

        int GetPlayerIndex(string buttonName)
        {
            lock (_lock)
            {
                int idx = _buttonOrder.IndexOf(buttonName);
                return idx >= 0 ? idx : 0;
            }
        }

        public object ProcessCommand(string cmd)
        {
            lock (_lock)
            {
                switch (cmd)
                {
                    case "arm":
                        if (Phase == Phase.Waiting || Phase == Phase.Correct || Phase == Phase.Wrong)
                        {
                            Phase = Phase.Armed;
                            Winner = -1;
                            Console.WriteLine(L.Get("QuizArmed"));
                            return new { @event = "armed" };
                        }
                        break;

                    case "correct":
                        if (Phase == Phase.Buzzed && Winner >= 0)
                        {
                            Scores[Winner]++;
                            Phase = Phase.Correct;
                            Console.WriteLine(L.Get("QuizCorrect", Winner + 1, Scores[Winner]));
                            return new { @event = "correct", winner = Winner, scores = Scores };
                        }
                        break;

                    case "wrong":
                        if (Phase == Phase.Buzzed && Winner >= 0)
                        {
                            Phase = Phase.Wrong;
                            Console.WriteLine(L.Get("QuizWrong", Winner + 1));
                            return new { @event = "wrong", winner = Winner, scores = Scores };
                        }
                        break;

                    case "reset":
                        Phase = Phase.Waiting;
                        Winner = -1;
                        Console.WriteLine(L.Get("QuizReset"));
                        return new { @event = "reset" };

                    case "reset_scores":
                        Scores[0] = 0;
                        Scores[1] = 0;
                        Phase = Phase.Waiting;
                        Winner = -1;
                        Console.WriteLine(L.Get("QuizResetScores"));
                        return new { @event = "reset_scores", scores = Scores };

                        case "lang_de":
                            L.SetLanguage("de");
                            for (int i = 0; i < Math.Min(_buttonOrder.Count, 2); i++)
                                Names[i] = $"{L.Get(i == 0 ? "Player1" : "Player2")}  [{_buttonOrder[i]}]";
                            if (_buttonOrder.Count == 0) { Names[0] = L.Get("Player1"); Names[1] = L.Get("Player2"); }
                            return new { @event = "strings", strings = L.UiStrings(), names = Names };

                        case "lang_en":
                            L.SetLanguage("en");
                            for (int i = 0; i < Math.Min(_buttonOrder.Count, 2); i++)
                                Names[i] = $"{L.Get(i == 0 ? "Player1" : "Player2")}  [{_buttonOrder[i]}]";
                            if (_buttonOrder.Count == 0) { Names[0] = L.Get("Player1"); Names[1] = L.Get("Player2"); }
                            return new { @event = "strings", strings = L.UiStrings(), names = Names };
                }
            }
            return null;
        }

        public object GetInitState() => new
        {
            @event = "init",
            names = Names,
            scores = Scores,
            phase = Phase.ToString().ToLower(),
            strings = L.UiStrings()
        };
    }

    // WebSocket and HTTP server

    class WebSocketServer
    {
        readonly QuizGame _game;
        readonly List<WebSocket> _clients = new List<WebSocket>();
        readonly object _clientLock = new object();

        // The HTML is embedded as a resource; we read it once and patch in the WS URL.
        static readonly string _html = LoadHtml();

        public WebSocketServer(QuizGame game) => _game = game;

        static string LoadHtml()
        {
            var asm = Assembly.GetExecutingAssembly();
            // Resource name: <AssemblyName>.<filename>
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

            while (true)
            {
                var ctx = await listener.GetContextAsync();

                if (ctx.Request.IsWebSocketRequest)
                    _ = Task.Run(() => HandleClientAsync(ctx));
                else
                    _ = Task.Run(() => ServeHttp(ctx));
            }
        }

        // Serve the embedded HTML for any non-WS GET request
        void ServeHttp(HttpListenerContext ctx)
        {
            try
            {
                var path = ctx.Request.Url.AbsolutePath.TrimStart('/');

                // Statische Assets aus wwwroot
                if (path.StartsWith("wwwroot/") || path.StartsWith("css/") || path.StartsWith("fonts/") || path.StartsWith("img/") || path.StartsWith("js/"))
                {
                    var resourceName = $"Klaxio.wwwroot.{path.Replace('/', '.')}";
                    var stream = Assembly.GetExecutingAssembly()
                                         .GetManifestResourceStream(resourceName);
                    if (stream != null)
                    {
                        ctx.Response.ContentType = GetMimeType(path);
                        ctx.Response.ContentLength64 = stream.Length;
                        stream.CopyTo(ctx.Response.OutputStream);
                        ctx.Response.Close();
                        return;
                    }
                }

                // Fallback: HTML ausliefern
                var bytes = Encoding.UTF8.GetBytes(_html);
                ctx.Response.ContentType = "text/html; charset=utf-8";
                ctx.Response.ContentLength64 = bytes.Length;
                ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
                ctx.Response.Close();
            }
            catch { }
        }

        static string GetMimeType(string path) => Path.GetExtension(path) switch
        {
            ".css"   => "text/css",
            ".js"    => "application/javascript",
            ".woff2" => "font/woff2",
            ".woff"  => "font/woff",
            ".ttf"   => "font/ttf",
            ".svg"   => "image/svg+xml",
            ".ico"   => "image/x-icon",
            _        => "application/octet-stream"
        };

        async Task HandleClientAsync(HttpListenerContext ctx)
        {
            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            var ws = wsCtx.WebSocket;

            Console.WriteLine(L.Get("WsClientConn", ctx.Request.RemoteEndPoint));
            lock (_clientLock) _clients.Add(ws);

            await SendAsync(ws, _game.GetInitState());

            var buffer = new byte[4096];
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
                            var response = _game.ProcessCommand(cmdEl.GetString());
                            if (response != null) await BroadcastAsync(response);
                        }
                    }
                    catch { }
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
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            var segment = new ArraySegment<byte>(bytes);

            List<WebSocket> snapshot;
            lock (_clientLock) snapshot = new List<WebSocket>(_clients);

            foreach (var ws in snapshot)
            {
                try { if (ws.State == WebSocketState.Open) await ws.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None); }
                catch { }
            }
        }

        async Task SendAsync(WebSocket ws, object message)
        {
            var json = JsonSerializer.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        void ConsoleInputLoop()
        {
            while (true)
            {
                var key = Console.ReadKey(intercept: true).Key;
                string cmd = key switch
                {
                    ConsoleKey.A => "arm",
                    ConsoleKey.C => "correct",
                    ConsoleKey.W => "wrong",
                    ConsoleKey.R => "reset",
                    _ => null
                };

                if (key == ConsoleKey.Q)
                {
                    Console.WriteLine(L.Get("Quitting"));
                    Environment.Exit(0);
                }

                if (cmd != null)
                {
                    var response = _game.ProcessCommand(cmd);
                    if (response != null) BroadcastAsync(response).GetAwaiter().GetResult();
                }
            }
        }
    }

    // Entry point
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine(L.Get("AppTitle"));
            Console.WriteLine();

            var game = new QuizGame();
            var server = new WebSocketServer(game);
            game.WebSocketServer = server;

            game.InitButtons();
            await server.StartAsync("http://localhost:8765/");
        }
    }
}

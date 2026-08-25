using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using EchoButtons;

namespace Klaxio
{
    /// <summary>
    /// Owns the Echo Buttons, the shared player roster and both game state
    /// machines. Every mutation happens under a single lock and returns the
    /// event object that should be broadcast to all connected browsers.
    /// </summary>
    class GameHub
    {
        public WebSocketServer WebSocketServer { get; set; }

        readonly List<Player> _players = new List<Player>();
        readonly MusicConfig  _music   = new MusicConfig();
        readonly object       _lock    = new object();

        EchoButton _echoButton;

        AppMode _mode = AppMode.Quiz;
        bool    _registerOpen;

        // Klaxio (classic)
        QuizPhase _quizPhase = QuizPhase.Waiting;
        Player    _quizWinner;

        // Klaxio Music
        MusicPhase _musicPhase = MusicPhase.Idle;
        int        _musicRound;
        Player     _musicBuzzer;
        DateTime   _musicBuzzedAt = DateTime.MinValue;

        /// <summary>Grace period after a buzz during which further presses are ignored.</summary>
        const int BuzzGraceMs = 1500;

        // Echo Buttons
        public void InitButtons()
        {
            _echoButton = new EchoButton();

            _echoButton.FoundPairedDevice += (s, e) => Console.WriteLine(L.Get("BtFound", e.ButtonName));
            _echoButton.NoPairedDevices   += (s, e) => Console.WriteLine(L.Get("BtNone"));
            _echoButton.Connected         += (s, e) => Console.WriteLine(L.Get("BtConnected", e.ButtonName));
            _echoButton.Disconnected      += (s, e) => Console.WriteLine(L.Get("BtDisconnected", e.ButtonName));
            _echoButton.Pressed           += OnButtonPressed;
            _echoButton.Released          += (s, e) => { };

            _echoButton.StartListening();
            Console.WriteLine(L.Get("BtSearching"));
        }

        void OnButtonPressed(object sender, ButtonEventArgs e)
        {
            object broadcast;
            lock (_lock)
            {
                if (_registerOpen)              broadcast = HandleRegisterPress(e.ButtonName);
                else if (_mode == AppMode.Quiz) broadcast = HandleQuizPress(e.ButtonName);
                else                            broadcast = HandleMusicPress(e.ButtonName);
            }
            if (broadcast != null)
                WebSocketServer?.BroadcastAsync(broadcast).GetAwaiter().GetResult();
        }

        /// <summary>While the registration screen is open a press claims a seat.</summary>
        object HandleRegisterPress(string buttonName)
        {
            var known = _players.FirstOrDefault(p => p.Button == buttonName);
            if (known != null)
            {
                // Already registered - just let the UI highlight the existing seat.
                return new { @event = "button_known", id = known.Id };
            }

            var player = new Player(L.Get("PlayerN", _players.Count + 1), buttonName);
            _players.Add(player);
            Console.WriteLine(L.Get("BtnRegistered", buttonName, player.Name));

            return new { @event = "player_registered", id = player.Id, players = PlayerDtos() };
        }

        object HandleQuizPress(string buttonName)
        {
            if (_quizPhase != QuizPhase.Armed) return null;

            var player = ActivePlayerForButton(buttonName);
            if (player == null) return null;

            _quizWinner = player;
            _quizPhase  = QuizPhase.Buzzed;
            Console.WriteLine(L.Get("BtnPressed", buttonName, player.Name));

            return BuildQuizState();
        }

        object HandleMusicPress(string buttonName)
        {
            switch (_musicPhase)
            {
                case MusicPhase.Playing:
                    var player = ActivePlayerForButton(buttonName);
                    if (player == null) return null;

                    _musicBuzzer   = player;
                    _musicBuzzedAt = DateTime.UtcNow;
                    _musicPhase    = MusicPhase.Buzzed;
                    Console.WriteLine(L.Get("BtnBuzzed", player.Name));
                    return BuildMusicState();

                case MusicPhase.Buzzed:
                    // Ignore presses inside the grace period: two players hitting
                    // their buttons almost simultaneously must not skip past the
                    // "who buzzed" screen.
                    if ((DateTime.UtcNow - _musicBuzzedAt).TotalMilliseconds < BuzzGraceMs) return null;
                    _musicPhase = MusicPhase.Reveal;
                    return BuildMusicState();

                default:
                    return null;
            }
        }

        Player ActivePlayerForButton(string buttonName)
        {
            var player = _players.FirstOrDefault(p => p.Button == buttonName);
            return player != null && player.Active ? player : null;
        }

        // Commands from the browser / console
        public object ProcessCommand(string cmd, JsonElement? payload = null)
        {
            lock (_lock)
            {
                switch (cmd)
                {
                    // shared
                    case "set_mode":
                        _mode = Str(payload, "mode") == "music" ? AppMode.Music : AppMode.Quiz;
                        return new { @event = "mode_changed", mode = ModeName };

                    case "register_open":
                        _registerOpen = true;
                        return new { @event = "register_state", open = true, players = PlayerDtos() };

                    case "register_close":
                        _registerOpen = false;
                        return new { @event = "register_state", open = false, players = PlayerDtos() };

                    case "rename_player":
                    {
                        var target = FindPlayer(Str(payload, "id"));
                        var name   = Str(payload, "name");
                        if (target != null && !string.IsNullOrWhiteSpace(name))
                        {
                            target.Name       = name.Trim();
                            target.CustomName = true;
                        }
                        return new { @event = "players_updated", players = PlayerDtos() };
                    }

                    case "toggle_player":
                    {
                        var target = FindPlayer(Str(payload, "id"));
                        if (target != null) target.Active = !target.Active;
                        return new { @event = "players_updated", players = PlayerDtos() };
                    }

                    case "remove_player":
                    {
                        var target = FindPlayer(Str(payload, "id"));
                        if (target == null) return null;
                        _players.Remove(target);
                        if (_quizWinner == target)
                        {
                            _quizWinner = null;
                            if (_quizPhase == QuizPhase.Buzzed) _quizPhase = QuizPhase.Waiting;
                        }
                        if (_musicBuzzer == target)
                        {
                            _musicBuzzer = null;
                            if (_musicPhase == MusicPhase.Buzzed || _musicPhase == MusicPhase.Reveal)
                                _musicPhase = MusicPhase.Waiting;
                        }
                        RenumberDefaultNames();
                        return new { @event = "players_updated", players = PlayerDtos() };
                    }

                    case "lang_de":
                    case "lang_en":
                        L.SetLanguage(cmd == "lang_de" ? "de" : "en");
                        RenumberDefaultNames();
                        return new
                        {
                            @event  = "strings",
                            lang    = L.Lang,
                            strings = L.UiStrings(),
                            players = PlayerDtos()
                        };

                    // Klaxio (classic)
                    case "quiz_arm":
                        if (_quizPhase != QuizPhase.Waiting && _quizPhase != QuizPhase.Correct
                            && _quizPhase != QuizPhase.Wrong) return null;
                        if (ActivePlayers().Count < 1) return null;
                        _quizPhase  = QuizPhase.Armed;
                        _quizWinner = null;
                        Console.WriteLine(L.Get("QuizArmed"));
                        return BuildQuizState();

                    case "quiz_correct":
                        if (_quizPhase != QuizPhase.Buzzed || _quizWinner == null) return null;
                        _quizWinner.QuizScore++;
                        _quizPhase = QuizPhase.Correct;
                        Console.WriteLine(L.Get("QuizCorrect", _quizWinner.Name, _quizWinner.QuizScore));
                        return BuildQuizState(new[] { _quizWinner.Id });

                    case "quiz_wrong":
                        if (_quizPhase != QuizPhase.Buzzed || _quizWinner == null) return null;
                        _quizPhase = QuizPhase.Wrong;
                        Console.WriteLine(L.Get("QuizWrong", _quizWinner.Name));
                        return BuildQuizState();

                    case "quiz_reset":
                        _quizPhase  = QuizPhase.Waiting;
                        _quizWinner = null;
                        Console.WriteLine(L.Get("QuizReset"));
                        return BuildQuizState();

                    case "quiz_reset_scores":
                        _players.ForEach(p => p.QuizScore = 0);
                        _quizPhase  = QuizPhase.Waiting;
                        _quizWinner = null;
                        Console.WriteLine(L.Get("QuizResetScores"));
                        return BuildQuizState();

                    case "quiz_finish":
                        if (_quizPhase == QuizPhase.Over) return null;
                        _quizPhase  = QuizPhase.Over;
                        _quizWinner = null;
                        Console.WriteLine(L.Get("QuizFinished"));
                        return BuildQuizState();

                    case "quiz_restart":
                        _players.ForEach(p => p.QuizScore = 0);
                        _quizPhase  = QuizPhase.Waiting;
                        _quizWinner = null;
                        Console.WriteLine(L.Get("QuizResetScores"));
                        return BuildQuizState();

                    // Klaxio Music
                    case "music_config":
                    {
                        var url = Str(payload, "playlistUrl");
                        if (url != null) _music.PlaylistUrl = url.Trim();
                        var rounds = Int(payload, "rounds");
                        if (rounds.HasValue) _music.Rounds = Math.Max(1, Math.Min(50, rounds.Value));
                        Console.WriteLine(L.Get("MusicConfig", _music.PlaylistUrl, _music.Rounds));
                        return new { @event = "music_config", config = _music.ToDto() };
                    }

                    case "music_start":
                        if (ActivePlayers().Count < 2) return null;
                        if (string.IsNullOrWhiteSpace(_music.PlaylistUrl)) return null;
                        _players.ForEach(p => p.MusicScore = 0);
                        _musicRound  = 0;
                        _musicBuzzer = null;
                        _musicPhase  = MusicPhase.Waiting;
                        Console.WriteLine(L.Get("MusicStarted", _music.Rounds));
                        return BuildMusicState("start");

                    case "music_play":
                        if (_musicPhase != MusicPhase.Waiting) return null;
                        _musicBuzzer = null;
                        _musicRound++;
                        if (_musicRound > _music.Rounds)
                        {
                            _musicRound = _music.Rounds;
                            _musicPhase = MusicPhase.Over;
                            Console.WriteLine(L.Get("MusicOver"));
                            return BuildMusicState();
                        }
                        _musicPhase = MusicPhase.Playing;
                        Console.WriteLine(L.Get("MusicRoundStart", _musicRound, _music.Rounds));
                        return BuildMusicState();

                    case "music_reveal":
                        if (_musicPhase != MusicPhase.Buzzed) return null;
                        _musicPhase = MusicPhase.Reveal;
                        return BuildMusicState();

                    case "music_correct":
                    {
                        if (_musicPhase != MusicPhase.Reveal || _musicBuzzer == null) return null;
                        var buzzer = _musicBuzzer;
                        buzzer.MusicScore++;
                        _musicPhase  = MusicPhase.Waiting;
                        _musicBuzzer = null;
                        Console.WriteLine(L.Get("QuizCorrect", buzzer.Name, buzzer.MusicScore));
                        return BuildMusicState("correct", buzzer, new[] { buzzer.Id });
                    }

                    case "music_wrong":
                    {
                        if (_musicPhase != MusicPhase.Reveal || _musicBuzzer == null) return null;
                        var buzzer = _musicBuzzer;
                        var others = ActivePlayers().Where(p => p.Id != buzzer.Id).ToList();
                        others.ForEach(p => p.MusicScore++);
                        _musicPhase  = MusicPhase.Waiting;
                        _musicBuzzer = null;
                        Console.WriteLine(L.Get("QuizWrong", buzzer.Name));
                        return BuildMusicState("wrong", buzzer, others.Select(p => p.Id).ToArray());
                    }

                    case "music_skip":
                        if (_musicPhase != MusicPhase.Playing) return null;
                        _musicBuzzer = null;
                        _musicPhase  = MusicPhase.Waiting;
                        Console.WriteLine(L.Get("MusicSkip"));
                        return BuildMusicState("skip");

                    case "music_restart":
                        _players.ForEach(p => p.MusicScore = 0);
                        _musicRound  = 0;
                        _musicBuzzer = null;
                        _musicPhase  = MusicPhase.Waiting;
                        return BuildMusicState("start");

                    case "music_finish":
                        if (_musicPhase == MusicPhase.Idle || _musicPhase == MusicPhase.Over) return null;
                        _musicBuzzer = null;
                        _musicPhase  = MusicPhase.Over;
                        Console.WriteLine(L.Get("MusicOver"));
                        return BuildMusicState();
                }
            }
            return null;
        }

        /// <summary>Maps a console hotkey to the command of the currently active mode.</summary>
        public string HotkeyCommand(ConsoleKey key)
        {
            bool music = _mode == AppMode.Music;
            switch (key)
            {
                case ConsoleKey.A: return music ? "music_play"    : "quiz_arm";
                case ConsoleKey.C: return music ? "music_correct" : "quiz_correct";
                case ConsoleKey.W: return music ? "music_wrong"   : "quiz_wrong";
                case ConsoleKey.S: return music ? "music_skip"    : null;
                case ConsoleKey.R: return music ? "music_reveal"  : "quiz_reset";
                default:           return null;
            }
        }

        // Payload builders
        string ModeName => _mode == AppMode.Music ? "music" : "quiz";

        List<Player> ActivePlayers() => _players.Where(p => p.Active).ToList();

        object[] PlayerDtos() => _players.Select(p => p.ToDto()).ToArray();

        Player FindPlayer(string id) => _players.FirstOrDefault(p => p.Id == id);

        /// <summary>Keeps auto-generated names ("Player 3") in sync with order and language.</summary>
        void RenumberDefaultNames()
        {
            for (int i = 0; i < _players.Count; i++)
                if (!_players[i].CustomName)
                    _players[i].Name = L.Get("PlayerN", i + 1);
        }

        object BuildQuizState(string[] bump = null) => new
        {
            @event     = "quiz_state",
            phase      = _quizPhase.ToString().ToLowerInvariant(),
            winnerId   = _quizWinner == null ? null : _quizWinner.Id,
            winnerName = _quizWinner == null ? null : _quizWinner.Name,
            bump       = bump ?? Array.Empty<string>(),
            players    = PlayerDtos()
        };

        object BuildMusicState(string outcome = null, Player buzzer = null, string[] bump = null)
        {
            var who = _musicBuzzer ?? buzzer;
            return new
            {
                @event      = "music_state",
                phase       = _musicPhase.ToString().ToLowerInvariant(),
                outcome     = outcome,
                round       = _musicRound,
                totalRounds = _music.Rounds,
                buzzerId    = who == null ? null : who.Id,
                buzzerName  = who == null ? null : who.Name,
                bump        = bump ?? Array.Empty<string>(),
                players     = PlayerDtos()
            };
        }

        public object GetInitState()
        {
            lock (_lock)
            {
                return new
                {
                    @event       = "init",
                    lang         = L.Lang,
                    strings      = L.UiStrings(),
                    mode         = ModeName,
                    registerOpen = _registerOpen,
                    players      = PlayerDtos(),
                    quiz         = BuildQuizState(),
                    music        = BuildMusicState(),
                    config       = _music.ToDto()
                };
            }
        }

        // JSON helpers
        static string Str(JsonElement? payload, string name)
            => payload.HasValue && payload.Value.TryGetProperty(name, out var el)
               && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

        static int? Int(JsonElement? payload, string name)
            => payload.HasValue && payload.Value.TryGetProperty(name, out var el)
               && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var v) ? v : (int?)null;
    }
}

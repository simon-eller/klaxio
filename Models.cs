using System;
using System.Collections.Generic;

namespace Klaxio
{
    /// <summary>Which of the two games is currently on screen.</summary>
    enum AppMode { Quiz, Music }

    /// <summary>
    /// Klaxio (classic buzzer quiz).
    ///   Waiting  - standby, buzzers locked
    ///   Armed    - buzzers unlocked, first press wins
    ///   Buzzed   - somebody buzzed, host evaluates
    ///   Correct  - answer was right (point awarded)
    ///   Wrong    - answer was wrong
    ///   Over     - host ended the game, results are shown
    /// </summary>
    enum QuizPhase { Waiting, Armed, Buzzed, Correct, Wrong, Over }

    /// <summary>
    /// Klaxio Music.
    ///   Idle     - no game running (needs a playlist + start)
    ///   Waiting  - between rounds, host may start the next song
    ///   Playing  - a song is playing, nobody buzzed yet
    ///   Buzzed   - somebody buzzed, answer not revealed yet
    ///   Reveal   - song is revealed, host evaluates
    ///   Over     - all rounds played, results are shown
    ///
    /// Button presses are only honoured in Playing (buzz) and Buzzed (reveal).
    /// </summary>
    enum MusicPhase { Idle, Waiting, Playing, Buzzed, Reveal, Over }

    /// <summary>
    /// A registered participant. Players live for the whole session and are
    /// shared by both games; each game keeps its own score.
    /// </summary>
    class Player
    {
        public string Id     { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name   { get; set; }
        public string Button { get; }               // Echo Button that claimed this seat
        public bool   Active { get; set; } = true;

        /// <summary>False while the name is still the auto-generated "Player N" (so it follows the UI language).</summary>
        public bool CustomName { get; set; }

        public int QuizScore  { get; set; }
        public int MusicScore { get; set; }

        /// <summary>A seat only ever comes into being by pressing an Echo Button.</summary>
        public Player(string name, string button)
        {
            Name   = name;
            Button = button;
        }

        public object ToDto() => new
        {
            id         = Id,
            name       = Name,
            button     = Button,
            active     = Active,
            quizScore  = QuizScore,
            musicScore = MusicScore
        };
    }

    /// <summary>Klaxio Music settings, editable from the settings screen.</summary>
    class MusicConfig
    {
        public string PlaylistUrl { get; set; } = "";
        public int    Rounds      { get; set; } = 10;

        public object ToDto() => new { playlistUrl = PlaylistUrl, rounds = Rounds };
    }
}

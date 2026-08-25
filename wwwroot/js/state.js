(function () {
    'use strict';

    const URL_KEY = 'klaxio_lastPlaylistUrl';

    const state = {
        screen:  'screen-quiz',   // currently visible section
        mode:    'quiz',          // which game the buttons belong to: quiz | music
        connected: false,

        players: [],              // [{ id, name, button, active, quizScore, musicScore }]

        quiz: {
            phase:    'waiting',  // waiting | armed | buzzed | correct | wrong | over
            winnerId: null,
        },

        music: {
            phase:       'idle',  // idle | waiting | playing | buzzed | reveal | over
            round:       0,
            totalRounds: 10,
            buzzerId:    null,
        },

        config: { playlistUrl: '', rounds: 10 },

        registerOpen: false,

        // Klaxio Music track handling: metadata is kept hidden until the reveal
        currentTrack: null,       // { title, artist, thumb, videoId }
        trackRevealed: false,
        playedTracks: [],         // revealed tracks of the running game

        // Which game the results screen is showing, and where "back" leads
        resultsMode:    'quiz',
        lastGameScreen: 'screen-quiz',

        /** Score of a player in the currently rendered game. */
        scoreOf(player, mode) {
            return (mode || this.mode) === 'music' ? player.musicScore : player.quizScore;
        },

        activePlayers() { return this.players.filter(p => p.active); },

        findPlayer(id) { return this.players.find(p => p.id === id) || null; },

        /* Remember the last playlist so a restart does not mean retyping the URL. */
        savePlaylistUrl(url) {
            try { localStorage.setItem(URL_KEY, url || ''); } catch (e) { /* private mode */ }
        },
        loadPlaylistUrl() {
            try { return localStorage.getItem(URL_KEY) || ''; } catch (e) { return ''; }
        },

        resetTracks() {
            this.currentTrack  = null;
            this.trackRevealed = false;
            this.playedTracks  = [];
        },
    };

    window.appState = state;
})();

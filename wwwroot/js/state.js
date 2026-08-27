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
        skipRevealed: false,      // showing the answer of a song nobody guessed
        playedTracks: [],         // revealed tracks of the running game
                                  // [{ title, artist, thumb, videoId, skipped }]

        // Which game the results screen is showing, and where "back" leads
        resultsMode:    'quiz',
        lastGameScreen: 'screen-quiz',

        /** Score of a player in the currently rendered game. */
        scoreOf(player, mode) {
            return (mode || this.mode) === 'music' ? player.musicScore : player.quizScore;
        },

        activePlayers() { return this.players.filter(p => p.active); },

        findPlayer(id) { return this.players.find(p => p.id === id) || null; },

        /** True when the configured playlist lives on YouTube Music rather than plain YouTube. */
        isMusicPlaylist() {
            return (this.config.playlistUrl || '').indexOf('music.youtube.com') !== -1;
        },

        /** Deep link to a played track, on the same service the playlist came from. */
        trackUrl(videoId) {
            if (!videoId) return '';
            const host = this.isMusicPlaylist() ? 'music.youtube.com' : 'www.youtube.com';
            return `https://${host}/watch?v=${encodeURIComponent(videoId)}`;
        },

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
            this.skipRevealed  = false;
            this.playedTracks  = [];
        },
    };

    window.appState = state;
})();

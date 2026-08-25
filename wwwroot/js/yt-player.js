(function () {
    'use strict';

    /* Inject the YouTube IFrame API */
    (function injectScript() {
        const s = document.createElement('script');
        s.src = 'https://www.youtube.com/iframe_api';
        document.head.appendChild(s);
    })();

    let player        = null;
    let isReady       = false;
    let playlistIds   = [];
    let currentIndex  = 0;
    let playing       = false;
    let progressTimer = null;
    let metaEmittedFor = null;   // video id the "meta" event already fired for

    const listeners = {
        ready:    [],
        playing:  [],
        paused:   [],
        ended:    [],
        progress: [],   // { position, duration, pct }
        meta:     [],   // { title, artist, thumb, videoId }
        error:    [],   // { code, key }
    };

    // YouTube error code -> i18n key
    const ERROR_KEYS = {
        2:   'errYtInvalid',
        5:   'errYtHtml5',
        100: 'errYtNotFound',
        101: 'errYtEmbed',
        150: 'errYtEmbed',
    };

    function emit(event, data) {
        (listeners[event] || []).forEach(fn => {
            try { fn(data); } catch (e) { console.error(`[yt] listener ${event} failed`, e); }
        });
    }

    /* YouTube API callbacks */
    window.onYouTubeIframeAPIReady = function () {
        player = new YT.Player('yt-player', {
            height: '1', width: '1',
            playerVars: {
                autoplay:       0,
                controls:       0,
                disablekb:      1,
                fs:             0,
                modestbranding: 1,
                rel:            0,
                origin:         location.origin,
            },
            events: {
                onReady:       () => { isReady = true; console.log('[yt] ready'); emit('ready'); },
                onStateChange: onStateChange,
                onError:       e => emit('error', { code: e.data, key: ERROR_KEYS[e.data] || 'errYtOther' }),
            },
        });
    };

    function onStateChange(e) {
        switch (e.data) {
            case YT.PlayerState.PLAYING:
                playing = true;
                tryEmitMeta();
                startProgressTimer();
                emit('playing');
                break;

            case YT.PlayerState.PAUSED:
                playing = false;
                stopProgressTimer();
                emit('paused');
                break;

            case YT.PlayerState.ENDED:
                playing = false;
                stopProgressTimer();
                emit('ended');
                break;

            case YT.PlayerState.BUFFERING:
                emit('playing');
                break;
        }
    }

    /* Metadata */
    function readMeta() {
        if (!player || typeof player.getVideoData !== 'function') return null;
        try {
            const d  = player.getVideoData();
            const id = playlistIds[currentIndex % playlistIds.length] || d.video_id;
            return {
                title:   d.title  || '',
                artist:  d.author || '',
                thumb:   id ? `https://img.youtube.com/vi/${id}/mqdefault.jpg` : '',
                videoId: id,
            };
        } catch (e) {
            console.warn('[yt] readMeta failed', e);
            return null;
        }
    }

    function tryEmitMeta() {
        const meta = readMeta();
        if (!meta || !meta.videoId) return;
        if (meta.videoId === metaEmittedFor) return;
        metaEmittedFor = meta.videoId;
        emit('meta', meta);
    }

    /* Progress */
    function startProgressTimer() {
        stopProgressTimer();
        progressTimer = setInterval(() => {
            if (!player || !playing) return;
            try {
                const pos = player.getCurrentTime();
                const dur = player.getDuration();
                if (dur > 0) emit('progress', { position: pos, duration: dur, pct: pos / dur * 100 });
            } catch (e) { /* player not ready */ }
        }, 800);
    }

    function stopProgressTimer() {
        clearInterval(progressTimer);
        progressTimer = null;
    }

    /* Helpers */
    function extractPlaylistId(url) {
        try {
            const list = new URL(url.trim()).searchParams.get('list');
            if (list) return list;
        } catch (e) { /* not an absolute URL - fall through to the regex */ }
        const m = url.match(/[?&]list=([A-Za-z0-9_-]+)/);
        return m ? m[1] : null;
    }

    function shuffle(arr) {
        for (let i = arr.length - 1; i > 0; i--) {
            const j = Math.floor(Math.random() * (i + 1));
            [arr[i], arr[j]] = [arr[j], arr[i]];
        }
        return arr;
    }

    function waitReady(timeoutMs) {
        return new Promise((resolve, reject) => {
            if (isReady) return resolve();
            const t0 = Date.now();
            const iv = setInterval(() => {
                if (isReady) { clearInterval(iv); resolve(); }
                else if (Date.now() - t0 > timeoutMs) { clearInterval(iv); reject(new Error('errPlayerReady')); }
            }, 100);
        });
    }

    /* Public API */
    window.ytPlayer = {
        on(event, fn) {
            if (!listeners[event]) listeners[event] = [];
            listeners[event].push(fn);
        },

        isReady()      { return isReady; },
        isPlaying()    { return playing; },
        hasPlaylist()  { return playlistIds.length > 0; },
        trackCount()   { return playlistIds.length; },
        getMeta()      { return readMeta(); },

        /**
         * Load and shuffle a YouTube playlist.
         * Resolves with { count }; rejects with an Error whose message is an i18n key.
         */
        async loadPlaylist(url) {
            const id = extractPlaylistId(url || '');
            if (!id) throw new Error('errPlaylistId');

            try { await waitReady(5000); }
            catch (e) { throw new Error('errPlayerReady'); }

            player.cuePlaylist({ listType: 'playlist', list: id, index: 0 });

            // Give the player a moment to populate the playlist metadata
            await new Promise(r => setTimeout(r, 1500));

            const ids = player.getPlaylist();
            if (!ids || ids.length === 0) throw new Error('errPlaylistEmpty');

            playlistIds    = shuffle(ids.slice());
            currentIndex   = 0;
            metaEmittedFor = null;
            return { count: playlistIds.length };
        },

        playCurrent() {
            if (!isReady || !playlistIds.length) return;
            metaEmittedFor = null;                 // allow a fresh meta event
            player.loadVideoById({ videoId: playlistIds[currentIndex % playlistIds.length], startSeconds: 0 });
        },

        pause()  { if (player && playing) player.pauseVideo(); },
        resume() { if (player) player.playVideo(); },

        stop() {
            if (player) { try { player.stopVideo(); } catch (e) { /* not loaded */ } }
            stopProgressTimer();
            playing = false;
        },

        seekRelative(pct) {
            if (!isReady) return;
            try {
                const dur = player.getDuration();
                if (dur > 0) player.seekTo(pct * dur, true);
            } catch (e) { /* nothing loaded */ }
        },

        advance(delta) {
            if (!playlistIds.length) return;
            const step = delta == null ? 1 : delta;
            currentIndex = (currentIndex + step + playlistIds.length) % playlistIds.length;
        },

        reset() {
            this.stop();
            playlistIds    = [];
            currentIndex   = 0;
            metaEmittedFor = null;
        },
    };
})();

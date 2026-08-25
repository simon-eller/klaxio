(function () {
    'use strict';

    // data-i18n-<suffix> -> attribute it fills
    const ATTRS = [
        ['data-i18n-title',      'title'],
        ['data-i18n-aria-label', 'aria-label'],
        ['data-i18n-placeholder', 'placeholder'],
    ];

    const listeners = [];

    const i18n = {
        lang:    'en',
        strings: {},

        /** Translate a key, replacing {0}, {1}, ... with the given arguments. */
        t(key, ...args) {
            let str = this.strings[key];
            if (str == null) return `[${key}]`;
            args.forEach((arg, i) => { str = str.split(`{${i}}`).join(arg); });
            return str;
        },

        /** Store a fresh set of strings coming from the server and repaint the DOM. */
        apply(lang, strings) {
            if (lang)    this.lang    = lang;
            if (strings) this.strings = strings;
            this.applyToDom();
            listeners.forEach(fn => { try { fn(this.lang); } catch (e) { console.error(e); } });
        },

        onChange(fn) { listeners.push(fn); },

        applyToDom(root) {
            const scope = root || document;

            scope.querySelectorAll('[data-i18n]').forEach(el => {
                const value = this.strings[el.getAttribute('data-i18n')];
                if (value != null) el.textContent = value;
            });

            ATTRS.forEach(([dataAttr, target]) => {
                scope.querySelectorAll(`[${dataAttr}]`).forEach(el => {
                    const value = this.strings[el.getAttribute(dataAttr)];
                    if (value != null) el.setAttribute(target, value);
                });
            });

            const label = document.getElementById('lang-label');
            if (label) label.textContent = this.lang.toUpperCase();

            document.querySelectorAll('[data-lang]').forEach(el => {
                if (el.dataset.lang === this.lang) el.setAttribute('aria-current', 'true');
                else                               el.removeAttribute('aria-current');
            });

            document.documentElement.lang = this.lang;
        },
    };

    window.i18n = i18n;
})();

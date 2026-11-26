using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Shell;
using System.Windows.Interop;
using WpfApp1.PasswordManager;
using System.Windows.Controls.Primitives;

namespace WpfApp1;

public partial class MainWindow
{
    // Injected into every page. It remembers scroll position, typed input and video position,
    // and puts them back after a reload (soft refresh), including on pages that build their
    // content late like YouTube.
    const string PagePersistenceScript = """
    (() => {
        'use strict';
        try {
            if (window.top !== window) return;                 // main frame only
            if (window.__tryxtorPersistence) return;
            window.__tryxtorPersistence = true;

            const KEY = '__tryxtor_page_state__';
            const MAX_AGE_MS = 60 * 60 * 1000;                 // ignore saved state older than an hour
            const FIELD_RETRY_MS = 6000;                       // keep re-applying inputs for the first seconds (SPAs re-render)
            const RESTORE_WINDOW_MS = 15000;                   // stop trying to restore after this long
            const MAX_FIELDS = 200;
            const SCROLL_KEYS = new Set(['PageDown', 'PageUp', 'Home', 'End', 'ArrowDown', 'ArrowUp', ' ']);
            const TEXT_TYPES = new Set(['', 'text', 'search', 'email', 'url', 'tel', 'number', 'date', 'time', 'datetime-local', 'month', 'week']);
            const SENSITIVE = /pass|pwd|cvv|cvc|card|ccnum|cc-|otp|one-time|token|secret|ssn|\bpin\b/i;

            const nav = (performance.getEntriesByType && performance.getEntriesByType('navigation')[0]) || null;
            const isReload = (nav && nav.type === 'reload') || (performance.navigation && performance.navigation.type === 1);

            const touchedFields = new WeakSet();               // inputs the user has edited since load
            const scrolledElements = new Set();                // scrollable containers the user has scrolled
            const pending = { win: false, els: new Set(), fields: new Set(), video: false };
            const attempts = new Map();
            let restoreState = null;

            // ---- helpers -------------------------------------------------------
            const cssPath = (el) => {
                const parts = [];
                while (el && el.nodeType === 1 && el !== document.documentElement) {
                    if (el.id) {
                        const selector = '#' + CSS.escape(el.id);
                        if (document.querySelectorAll(selector).length === 1) { parts.unshift(selector); break; }
                    }
                    let part = el.tagName.toLowerCase();
                    const parent = el.parentElement;
                    if (parent) {
                        const same = Array.prototype.filter.call(parent.children, c => c.tagName === el.tagName);
                        if (same.length > 1) part += ':nth-of-type(' + (same.indexOf(el) + 1) + ')';
                    }
                    parts.unshift(part);
                    el = parent;
                }
                return parts.join(' > ');
            };

            const isSensitive = (el) => SENSITIVE.test([el.name, el.id, el.autocomplete, el.getAttribute('aria-label')].join(' '));

            const pickVideo = () => {
                if (document.querySelector('.ad-showing, .ad-interrupting')) return null;   // don't save ad playback
                let best = null;
                for (const v of document.querySelectorAll('video')) {
                    if (!isFinite(v.duration) || v.duration < 45 || v.currentTime < 3 || v.ended) continue;
                    if (!best || v.duration > best.duration) best = v;
                }
                return best;
            };

            const readField = (el) => {
                if (el instanceof HTMLInputElement) {
                    const type = (el.type || '').toLowerCase();
                    if (type === 'checkbox' || type === 'radio') return el.checked !== el.defaultChecked ? { c: el.checked } : null;
                    if (!TEXT_TYPES.has(type) || isSensitive(el)) return null;
                    return el.value !== el.defaultValue && el.value.length <= 20000 ? { v: el.value } : null;
                }
                if (el instanceof HTMLTextAreaElement) {
                    if (isSensitive(el)) return null;
                    return el.value !== el.defaultValue && el.value.length <= 20000 ? { v: el.value } : null;
                }
                if (el instanceof HTMLSelectElement) {
                    const now = Array.from(el.options, (o, i) => o.selected ? i : -1).filter(i => i >= 0);
                    const def = Array.from(el.options, (o, i) => o.defaultSelected ? i : -1).filter(i => i >= 0);
                    return JSON.stringify(now) !== JSON.stringify(def) ? { s: now } : null;
                }
                if (el.isContentEditable && el === document.activeElement) {
                    const text = (el.innerText || '').slice(0, 20000);
                    return text.trim() ? { t: text } : null;
                }
                return null;
            };

            // ---- saving --------------------------------------------------------
            const collect = () => {
                const state = {
                    href: location.href,
                    time: Date.now(),
                    x: Math.round(window.scrollX),
                    y: Math.round(window.scrollY),
                    els: {},
                    fields: {},
                    video: null
                };

                let scrolled = 0;
                scrolledElements.forEach(el => {
                    if (scrolled >= 50 || !el.isConnected || !(el.scrollTop > 0 || el.scrollLeft > 0)) return;
                    const path = cssPath(el);
                    if (path) { state.els[path] = [Math.round(el.scrollLeft), Math.round(el.scrollTop)]; scrolled++; }
                });

                let count = 0;
                for (const el of document.querySelectorAll('input, textarea, select, [contenteditable]')) {
                    if (count >= MAX_FIELDS) break;
                    const field = readField(el);
                    if (!field) continue;
                    const path = cssPath(el);
                    if (!path) continue;
                    if (el === document.activeElement) {
                        field.focus = 1;
                        try { if (typeof el.selectionStart === 'number') { field.ss = el.selectionStart; field.se = el.selectionEnd; } } catch (_) { }
                    }
                    state.fields[path] = field;
                    count++;
                }

                const video = pickVideo();
                if (video) state.video = { t: video.currentTime, d: video.duration, p: video.paused };
                return state;
            };

            // While a restore is still in progress the page may not have its content yet,
            // so keep whatever has not been restored from the previous state.
            const mergePending = (state) => {
                if (!restoreState) return state;
                if (pending.win) { state.x = restoreState.x; state.y = restoreState.y; }
                pending.els.forEach(p => { if (!(p in state.els) && restoreState.els[p]) state.els[p] = restoreState.els[p]; });
                pending.fields.forEach(p => { if (!(p in state.fields) && restoreState.fields[p]) state.fields[p] = restoreState.fields[p]; });
                if (pending.video && restoreState.video) state.video = restoreState.video;
                return state;
            };

            const save = () => {
                try { sessionStorage.setItem(KEY, JSON.stringify(mergePending(collect()))); } catch (_) { }
            };
            window.__tryxtorSaveState = save;

            let timer = 0;
            const schedule = () => {
                if (timer) return;
                timer = setTimeout(() => { timer = 0; save(); }, 350);
            };

            window.addEventListener('scroll', schedule, { passive: true });
            document.addEventListener('scroll', (e) => {
                const target = e.target;
                if (target && target.nodeType === 1) { scrolledElements.add(target); schedule(); }
            }, true);

            const userEdited = (e) => {
                if (e.isTrusted && e.target) {
                    touchedFields.add(e.target);
                    try { pending.fields.delete(cssPath(e.target)); } catch (_) { }
                }
                schedule();
            };
            document.addEventListener('input', userEdited, true);
            document.addEventListener('change', userEdited, true);

            window.addEventListener('pagehide', save);
            window.addEventListener('beforeunload', save);
            document.addEventListener('visibilitychange', () => { if (document.visibilityState === 'hidden') save(); });
            setInterval(() => { if (!document.hidden && pickVideo()) schedule(); }, 5000);

            // ---- restoring (only after a reload) ---------------------------------
            const setNativeValue = (el, value) => {
                const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype
                    : el instanceof HTMLSelectElement ? HTMLSelectElement.prototype
                    : HTMLInputElement.prototype;
                const descriptor = Object.getOwnPropertyDescriptor(proto, 'value');
                if (descriptor && descriptor.set) descriptor.set.call(el, value); else el.value = value;
            };
            const fire = (el, type) => el.dispatchEvent(new Event(type, { bubbles: true }));

            const writeField = (el, field, path) => {
                const tries = attempts.get(path) || 0;
                if (tries >= 3) return;                               // some frameworks fight back: give up quietly

                if ('c' in field) {
                    if (el.checked === field.c || (el.type === 'radio' && !field.c)) return;
                    attempts.set(path, tries + 1);
                    el.click();
                    return;
                }
                if ('s' in field && el instanceof HTMLSelectElement) {
                    const now = Array.from(el.options, (o, i) => o.selected ? i : -1).filter(i => i >= 0);
                    if (JSON.stringify(now) === JSON.stringify(field.s)) return;
                    attempts.set(path, tries + 1);
                    Array.from(el.options).forEach((o, i) => { o.selected = field.s.includes(i); });
                    fire(el, 'input'); fire(el, 'change');
                    return;
                }
                if ('v' in field) {
                    if (el.value === field.v) return;
                    if (el.value !== el.defaultValue && el.value !== '') return;   // the page already filled it with something else
                    attempts.set(path, tries + 1);
                    setNativeValue(el, field.v);
                    fire(el, 'input'); fire(el, 'change');
                    if (field.focus && (!document.activeElement || document.activeElement === document.body)) {
                        try {
                            el.focus({ preventScroll: true });
                            if (typeof field.ss === 'number' && el.setSelectionRange) el.setSelectionRange(field.ss, field.se);
                        } catch (_) { }
                    }
                    return;
                }
                if ('t' in field && el.isContentEditable) {
                    if ((el.innerText || '').trim()) return;
                    attempts.set(path, tries + 1);
                    el.focus({ preventScroll: true });
                    document.execCommand('insertText', false, field.t);
                }
            };

            const startRestore = (saved) => {
                restoreState = saved;
                pending.win = !!(saved.x || saved.y);
                Object.keys(saved.els || {}).forEach(p => pending.els.add(p));
                Object.keys(saved.fields || {}).forEach(p => pending.fields.add(p));
                pending.video = !!saved.video;

                // As soon as the user scrolls, stop moving the page for them.
                const userScrolled = (e) => {
                    if (e.type === 'keydown' && !SCROLL_KEYS.has(e.key)) return;
                    pending.win = false;
                };
                ['wheel', 'touchstart', 'keydown', 'mousedown'].forEach(type =>
                    window.addEventListener(type, userScrolled, { capture: true, passive: true }));

                const started = Date.now();
                let stableSince = 0;

                const step = () => {
                    const elapsed = Date.now() - started;

                    if (pending.win) {
                        const off = Math.abs(window.scrollY - saved.y) > 2 || Math.abs(window.scrollX - saved.x) > 2;
                        if (off) {
                            stableSince = 0;
                            window.scrollTo({ left: saved.x, top: saved.y, behavior: 'instant' });
                        } else {
                            if (!stableSince) stableSince = Date.now();
                            if (Date.now() - stableSince > 1500) pending.win = false;   // held position long enough
                        }
                    }

                    for (const path of Array.from(pending.els)) {
                        let el = null;
                        try { el = document.querySelector(path); } catch (_) { pending.els.delete(path); continue; }
                        if (!el) continue;
                        const pos = saved.els[path];
                        el.scrollTo({ left: pos[0], top: pos[1], behavior: 'instant' });
                        if (Math.abs(el.scrollTop - pos[1]) <= 2) pending.els.delete(path);
                    }

                    for (const path of Array.from(pending.fields)) {
                        let el = null;
                        try { el = document.querySelector(path); } catch (_) { pending.fields.delete(path); continue; }
                        if (!el) continue;
                        if (touchedFields.has(el)) { pending.fields.delete(path); continue; }
                        writeField(el, saved.fields[path], path);
                    }
                    if (elapsed > FIELD_RETRY_MS) pending.fields.clear();

                    if (pending.video && saved.video) {
                        const v = Array.from(document.querySelectorAll('video')).find(x => isFinite(x.duration) && Math.abs(x.duration - saved.video.d) < 2);
                        if (v && v.readyState >= 1) {
                            if (v.currentTime < saved.video.t - 2) v.currentTime = saved.video.t;
                            if (saved.video.p) { try { v.pause(); } catch (_) { } }
                            else if (v.paused) { try { v.play().catch(() => { }); } catch (_) { } }
                            pending.video = false;
                        }
                    }

                    const finished = !pending.win && pending.els.size === 0 && pending.fields.size === 0 && !pending.video;
                    if (finished || elapsed > RESTORE_WINDOW_MS) {
                        clearInterval(handle);
                        restoreState = null;
                        pending.win = false; pending.els.clear(); pending.fields.clear(); pending.video = false;
                        schedule();
                    }
                };
                const handle = setInterval(step, 120);
            };

            if (isReload) {
                try {
                    const raw = sessionStorage.getItem(KEY);
                    const saved = raw ? JSON.parse(raw) : null;
                    if (saved && saved.href === location.href && Date.now() - saved.time < MAX_AGE_MS) startRestore(saved);
                } catch (_) { }
            }
        } catch (_) { /* never break the page */ }
    })();
    """;
}

/* SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com> */
/* SPDX-License-Identifier: MIT */

/*! /vim.js - modal (vim-style) editing for textareas, as a progressive enhancement.
 *
 *  A native ES module, loaded as <script type="module" src="/vim.js">. Nothing is
 *  published on window: `attach`, `autostart` and `isIdle` are the editor, and the
 *  default export is that group.
 *
 *  This is everything an install has to load, and the only file /notes loads: it
 *  imports this, gets the keys and the statusline on its textarea, and asks isIdle()
 *  when Escape might belong to its own dialog. Nothing here knows what a switch
 *  looks like, where a visitor's answer is kept, or what a page says while the keys
 *  are off - because there is no "off" here: a page that wanted them not sent did
 *  not send this file, and a page that carries a switch of its own is carrying it
 *  for itself.
 *
 *  One file, in dependency order, and each banner below is one section of it. They
 *  were twelve modules once; they are sections now because the split bought nothing
 *  the browser could use - only the geometry was ever shared by more than two
 *  consumers, every page that loads the editor loads all of it, and the import
 *  chain ran four deep before the first textarea was touched. Read them in order:
 *
 *    geometry     lines, offsets, words, blanks - pure arithmetic, no DOM
 *    state        the mode, the count, the pending operator, the ex line
 *    register     the unnamed register, and the clipboard that mirrors it
 *    buffer       writing into the textarea, and the browser's undo stack
 *    statusline   the three spans under the textarea, and the bar that holds them
 *    motions      where a motion ends, exclusively and inclusively
 *    insert       i, a, I, A, o, O, Enter, Tab and Shift+Tab
 *    operators    d, c, y, x, p, J - the ranges they take and what they say
 *    ex           the command line: addresses, :w, and the prompt
 *    search       /, ?, n, N, * and # - the pattern, and where it lands
 *    keys         the keydown grammar that dispatches to all of the above
 *    editor       one attached textarea, and the handle the rest pass around
 *    entry        what a page imports: attach, autostart and isIdle
 *
 *  Every section after the geometry works through the editor handle that the editor
 *  section builds, so the dependency runs one way: geometry, motions and operators
 *  are leaves, search, keys and ex are branches, and the editor is the root that
 *  knows about all of them at once. That is why the sections sit in this order, and
 *  why nothing here needs a hoisted import.
 *
 *  attach(textarea) reads and writes .value and the selection. It never replaces
 *  the element and never touches the surrounding form, so the no-JS fallback is
 *  simply the textarea that was always there. A browser that cannot parse this
 *  file runs none of it and lands in exactly the same place.
 *
 *  Loading this file is the whole of the decision to have these keys, and it is
 *  taken before the file is sent: a page that wants a plain textarea does not ship
 *  it, so what that visitor gets is the textarea the server sent - not an
 *  early-returning key handler pretending to be off, and nothing a screen reader or
 *  a validator could tell apart. The control is the server's, and it lives on
 *  /editor-mode; this file has no opinion about whether it should have arrived.
 *
 *  Registers: one unnamed register holds everything taken out of the buffer -
 *  yanks, deletes and changes alike - and the system clipboard is a mirror of it,
 *  so any command that changes the register also changes the clipboard. Named
 *  registers would be the way to keep a yank across a delete; see the backlog on
 *  /vim.
 *
 *  Types: there is no build step here, so the annotations are JSDoc. An editor or
 *  a type checker reads them and the browser never sees them, which means a wrong
 *  annotation can never break the page the way a wrong type could.
 */


/* ---------- geometry: lines, offsets, words, blanks ----------
 *
 *  Nothing here knows about modes, registers, the DOM or a textarea. Every
 *  function takes a string - or a line index already built from one - and answers
 *  with a number, so the motions and the operators can agree about where a line
 *  ends without either of them owning the rule.
 *
 *  The only state is createLineIndex(), which keeps the line starts beside the
 *  value they were built from, so a command that asks twice pays once and no edit
 *  has to remember to invalidate anything.
 */

/**
 * What counts as a word character for the small-word motions. Everything else is
 * punctuation, which is why `w` stops on a comma and `W` walks past it.
 * @type {RegExp}
 */
const WORD = /[A-Za-z0-9_]/;

/**
 * A character class, as classify() reports it: 0 for a blank, 1 for a word
 * character - or for any non-blank under the WORD motions, where punctuation
 * counts as part of a word - and 2 for punctuation.
 * @typedef {0 | 1 | 2} CharClass
 */

/**
 * Whether a character is blank: nothing at all, a space, a tab, or a line ending.
 * @param {string} c
 * @returns {boolean}
 */
const isBlank = (c) => c === "" || c === " " || c === "\t" || c === "\n" || c === "\r";

/**
 * Sort one character into a class, so the word motions can say "the same sort of
 * character as this one" without ever naming the sort.
 * @param {string} c   the character, possibly "" past the end of the buffer
 * @param {boolean} big true for the WORD motions, where punctuation is a word
 *   character and there is no second sort to distinguish
 * @returns {CharClass}
 */
function classify(c, big) {
    if (!c || isBlank(c)) { return 0; }
    if (big) { return 1; }
    return WORD.test(c) ? 1 : 2;
}

/**
 * Every offset at which a line starts, so a line number and an offset can be
 * translated in either direction. Line 0 starts at 0 even in an empty buffer,
 * which is why there is always at least one line.
 * @param {string} v the buffer
 * @returns {number[]} ascending starts, one per line
 */
function lineStarts(v) {
    const starts = [0];
    for (let i = 0; i < v.length; i += 1) {
        if (v.charCodeAt(i) === 10) { starts.push(i + 1); }
    }
    return starts;
}

/**
 * Which line an offset is on, by binary search: the last start that is not past
 * it wins, so the newline ending a line belongs to that line.
 * @param {number[]} starts from lineStarts()
 * @param {number} pos
 * @returns {number} the 0-based line index
 */
function lineIndexOf(starts, pos) {
    let lo = 0;
    let hi = starts.length - 1;
    while (lo < hi) {
        const mid = (lo + hi + 1) >> 1;
        if (starts[mid] <= pos) { lo = mid; } else { hi = mid - 1; }
    }
    return lo;
}

/**
 * The offset of the newline that ends a line, or the length of the buffer for the
 * last line, which has none. The offset is exclusive: the newline is not a
 * character the caret may rest on.
 * @param {string} v
 * @param {number[]} starts from lineStarts()
 * @param {number} index the 0-based line index
 * @returns {number}
 */
const lineEndOf = (v, starts, index) =>
    index + 1 < starts.length ? starts[index + 1] - 1 : v.length;

/**
 * The position on a line that is `column` characters in, folded onto a character
 * the caret may occupy.
 * @param {string} v
 * @param {number[]} starts from lineStarts()
 * @param {number} line the 0-based line index
 * @param {number} column the column aimed for, which may be past the end of the
 *   line the caret has walked up or down to
 * @returns {number}
 */
const caretOnLine = (v, starts, line, column) => {
    const start = starts[line];
    const end = lineEndOf(v, starts, line);
    const target = start + Math.max(0, column);
    /* A column past the end of a short line must not leave the caret on the
       newline, which is not a character it may rest on: step back onto the last
       character of that line. An empty line has none, so the caret stops at its
       start, which is also its end. */
    if (target < end) { return target; }
    return end > start ? end - 1 : end;
};

/**
 * ^ and I both want the first character of a line that is not indentation, which
 * is the end of the line for a blank one - the newline, an offset that caretAt()
 * then folds back onto something real.
 * @param {string} v
 * @param {number[]} starts from lineStarts()
 * @param {number} line
 * @returns {number}
 */
function firstNonBlank(v, starts, line) {
    const end = lineEndOf(v, starts, line);
    let c = starts[line];
    while (c < end && (v.charAt(c) === " " || v.charAt(c) === "\t")) { c += 1; }
    return c;
}

/**
 * The indentation of a line: the run of spaces and tabs it begins with, as the
 * characters themselves rather than as a count. Copying the run is what lets
 * autoindent keep whatever an author already has - a tab stays a tab, two spaces
 * stay two - and why no caller has to be told an indent width.
 *
 * A line of nothing but whitespace is all indentation, so its run is the whole line.
 * That is deliberate, and it is why this does not answer with firstNonBlank(): Enter
 * at the end of an indented empty line should leave the next one just as indented.
 * @param {string} v
 * @param {number[]} starts from lineStarts()
 * @param {number} line
 * @returns {string} the leading whitespace, "" when there is none
 */
function indentOf(v, starts, line) {
    const end = lineEndOf(v, starts, line);
    let c = starts[line];
    while (c < end && (v.charAt(c) === " " || v.charAt(c) === "\t")) { c += 1; }
    return v.slice(starts[line], c);
}

/**
 * The start of the word after the one at `pos`: past this run of characters, then
 * past the blanks that follow it. Answers with the length of the buffer when
 * there is no next word, which is the one offset a caret cannot take - so callers
 * that move the caret fold it back with caretAt().
 * @param {string} v
 * @param {number} pos
 * @param {boolean} big
 * @returns {number}
 */
function forwardWord(v, pos, big) {
    const n = v.length;
    let i = pos;
    if (i >= n) { return n; }
    const kind = classify(v.charAt(i), big);
    if (kind === 0) {
        while (i < n && classify(v.charAt(i), big) === 0) { i += 1; }
    } else {
        while (i < n && classify(v.charAt(i), big) === kind) { i += 1; }
    }
    while (i < n && classify(v.charAt(i), big) === 0) { i += 1; }
    return i;
}

/**
 * The start of the word before `pos`: back over the blanks, then back over the
 * run of same-class characters that ends the previous word.
 * @param {string} v
 * @param {number} pos
 * @param {boolean} big
 * @returns {number}
 */
function backWord(v, pos, big) {
    let i = pos;
    if (i <= 0) { return 0; }
    i -= 1;
    while (i > 0 && classify(v.charAt(i), big) === 0) { i -= 1; }
    const kind = classify(v.charAt(i), big);
    if (kind !== 0) {
        while (i > 0 && classify(v.charAt(i - 1), big) === kind) { i -= 1; }
    }
    return i;
}

/**
 * The last character of the word at or after `pos`: what `e` lands on, and what
 * an operator has to take one past.
 * @param {string} v
 * @param {number} pos
 * @param {boolean} big
 * @returns {number}
 */
function endWord(v, pos, big) {
    const n = v.length;
    if (n === 0) { return 0; }
    let i = pos + 1;
    if (i >= n) { return n - 1; }
    while (i < n && classify(v.charAt(i), big) === 0) { i += 1; }
    if (i >= n) { return n - 1; }
    const kind = classify(v.charAt(i), big);
    if (kind !== 0) {
        while (i + 1 < n && classify(v.charAt(i + 1), big) === kind) { i += 1; }
    }
    return i;
}

/**
 * A line index that rebuilds itself whenever the buffer changes. Almost every
 * command asks where the lines start, and an edit is the only thing that moves
 * them, so the cache is keyed on the very thing an edit changes: nothing has to
 * remember to throw it away.
 * @returns {(value: string) => number[]} the starts for that value, cached
 */
function createLineIndex() {
    /** @type {number[]} */
    let cachedStarts = [0];
    /* The value that index was built from. Null is not a possible .value, so the
       first call always rebuilds and nothing has to be invalidated. */
    /** @type {string | null} */
    let cachedValue = null;
    return (value) => {
        if (value !== cachedValue) {
            cachedStarts = lineStarts(value);
            cachedValue = value;
        }
        return cachedStarts;
    };
}


/* ---------- state: what a half-typed command is made of ----------
 *
 *  One plain object per attached textarea, and the two operations every grammar
 *  needs on it: forget everything (clearPending) and read the count typed so far
 *  (pendingCount). Keeping the clamp and the "no count is not a count" rule here
 *  means no command can disagree about what a count of zero means.
 *
 *  The prompt lives in here too: it sits beside the mode rather than in it, because
 *  a command line is not a mode. It is opened from normal mode - by `:` for a
 *  command, by `/` or `?` for a search - closed on Enter or Escape, and the mode
 *  underneath never changes while it is open. The last search is in here as well,
 *  and outlives every prompt, which is what `n` repeats.
 */

/**
 * The mode the buffer is being read in. The command line is deliberately not one
 * of these: see `VimState.ex`.
 * @typedef {"normal" | "insert"} VimMode
 */

/**
 * An operator, waiting for the motion that says how much of the buffer it takes.
 * @typedef {"d" | "c" | "y"} Operator
 */

/**
 * What a prompt was opened with, and what has been typed into it: `:` for an ex
 * command, `/` or `?` for a search in that direction. The kind is also the character
 * the statusline puts in front of the text, so a prompt reads the way vim's does.
 * @typedef {object} Prompt
 * @property {":" | "/" | "?"} kind  what opened it, and what Enter runs
 * @property {string} text           what has been typed so far
 */

/**
 * The last search, kept long after the command that typed it: the pattern as it was
 * typed, and the direction it ran in, which is what n repeats and N reverses.
 * @typedef {object} Search
 * @property {string} pattern   the regular expression the visitor typed
 * @property {boolean} forward  did it look towards the end of the buffer?
 */

/**
 * Everything a half-typed command is made of. clearPending() clears all of it, so
 * the fields together are "the command so far" - and nothing outside it is: the aim
 * below outlives a command, the way vim's curswant does, and so does the last search,
 * which is why n still knows what to repeat after a dd.
 * @typedef {object} VimState
 * @property {VimMode} mode       which mode the buffer is being read in
 * @property {string} count       digits typed so far, or "" for none
 * @property {Operator | null} op a pending d, c or y
 * @property {"g" | null} prefix  a pending g, waiting for its second key
 * @property {string} buffer      what the statusline echoes
 * @property {Prompt | null} prompt the command line or the search being typed
 * @property {Search | null} search the last search, which outlives the prompt
 * @property {Aim | null} aim     the column a run of j and k is aiming at
 */

/**
 * The column a vertical move is aiming at, and where it left the caret: vim's
 * curswant. The two are kept apart because a column is not a position - stepping
 * down over a short line and back up has to come back to the column the run began
 * at, and that is exactly the answer the caret cannot give once it has been folded
 * onto a line too short to hold it.
 *
 * The offset is what lets the aim expire without anyone having to clear it: h, a
 * word motion, a click and an edit all move the caret, and a caret that is not
 * where the last j or k left it is a caret something else has moved.
 * @typedef {object} Aim
 * @property {number} column the column to aim at, 0-based
 * @property {number} at     the caret offset the last vertical move left behind
 */

/**
 * The largest count that will be honoured, so a held-down digit cannot ask for a
 * number of repetitions that takes minutes.
 * @type {number}
 */
const MAX_COUNT = 10000;

/**
 * A fresh state: normal mode, nothing pending, nothing echoed.
 * @returns {VimState}
 */
function createState() {
    return {
        mode: "normal", count: "", op: null, prefix: null, buffer: "",
        prompt: null, search: null, aim: null,
    };
}

/**
 * Forget the half-typed command: the count, the operator, the g prefix, the echo,
 * and the prompt. Called after every command that ran, when the textarea loses
 * focus, and when a colon or a slash opens a prompt.
 *
 * Neither the aim nor the last search is one of them. The aim belongs to a run of
 * vertical moves rather than to the command that ran between them, and it carries its
 * own guard - the offset the run left the caret at - so it expires on the first h,
 * click or edit without anything here having to know that is what happened. The
 * search belongs to n and N, which are commands in their own right and have no
 * prompt to be forgotten with.
 *
 * It does not paint. Every caller already has something of its own to draw, and
 * the statusline is written by whoever knows what happened.
 *
 * @param {VimState} state
 * @returns {void}
 */
function clearPending(state) {
    state.count = "";
    state.op = null;
    state.prefix = null;
    state.buffer = "";
    state.prompt = null;
}

/**
 * The count typed so far, or 1. Everything that takes a count asks here, so "no
 * count is not a count" and the clamp live in one place.
 * @param {VimState} state
 * @returns {number} the count typed so far, at least 1 and never past MAX_COUNT
 */
function pendingCount(state) {
    if (state.count === "") { return 1; }
    const count = parseInt(state.count, 10);
    if (!count || count < 1) { return 1; }
    return count > MAX_COUNT ? MAX_COUNT : count;
}

/* ---------- register: the one unnamed register, and its clipboard mirror ----------
 *
 *  One unnamed register holds everything taken out of the buffer - yanks,
 *  deletes and changes alike - and the system clipboard is a mirror of it, so any
 *  command that changes the register also changes the clipboard.
 *
 *  Named registers would be the way to keep a yank across a delete; see the
 *  backlog on /vim. A native paste event is captured into the register for free
 *  (no permission needed); a bare "p" with an empty register reads the clipboard
 *  only if the clipboard-read permission has already been granted, so it never
 *  prompts in the middle of a command.
 *
 *  The handle is an object rather than a rebindable variable because several of
 *  the commands below hold on to it: take() and adopt() move the text, and the
 *  getters are the only way to read it back.
 */

/**
 * The one unnamed register: what was last taken out of the buffer, and whether it
 * was taken as whole lines, which is what decides how paste puts it back.
 * @typedef {object} Register
 * @property {string} text      exactly the characters taken, newlines included
 * @property {boolean} linewise true when the text is whole lines
 */

/**
 * The register as the rest of the editor sees it.
 * @typedef {object} RegisterHandle
 * @property {string} text        what the register holds right now
 * @property {boolean} linewise   whether that text is whole lines
 * @property {(text: string, linewise: boolean) => void} take take text out of
 *   the buffer: set the register and mirror it to the clipboard
 * @property {(text: string) => void} adopt take text that is already on the
 *   clipboard, so nothing is written back to it
 * @property {() => Promise<string | null>} readClipboard the clipboard's text,
 *   but only when permission was already granted
 */

/**
 * Mirror some text onto the system clipboard, best effort: the register has
 * already been updated, so a refusal here costs nothing but the mirror.
 * @param {string} text
 * @returns {void}
 */
function copyToSystem(text) {
    if (!text) { return; }
    try {
        /* Optional calls on both links short-circuit the whole chain, so a
           missing clipboard or writeText never reaches .catch. */
        navigator.clipboard?.writeText?.(text).catch(() => {
            /* no permission, or not a secure context */
        });
    } catch { /* clipboard unavailable */ }
}

/**
 * Read the system clipboard, resolving to null unless permission has already
 * been granted: a bare "p" must never trigger a permission prompt in the middle
 * of a command.
 * @returns {Promise<string | null>} the clipboard's text, or null
 */
async function readSystemClipboard() {
    const clip = navigator.clipboard;
    if (!clip || typeof clip.readText !== "function") { return null; }
    if (!navigator.permissions?.query) { return null; }
    try {
        const status = await navigator.permissions.query({ name: "clipboard-read" });
        if (status.state !== "granted") { return null; }
        return await clip.readText();
    } catch {
        return null;
    }
}

/**
 * A fresh, empty register.
 * @returns {RegisterHandle}
 */
function createRegister() {
    /** @type {Register} */
    let entry = { text: "", linewise: false };

    return {
        get text() { return entry.text; },
        get linewise() { return entry.linewise; },

        /* The register is the source of truth and copyToSystem is best effort,
           so a refused clipboard never costs you the text. */
        take(text, linewise) {
            entry = { text, linewise };
            if (text) { copyToSystem(text); }
        },

        adopt(text) {
            entry = { text, linewise: false };
        },

        readClipboard() {
            return readSystemClipboard();
        },
    };
}

/**
 * Capture native pastes into the register. A native paste is already in the
 * clipboard, so it costs nothing to keep a copy here: no permission is involved
 * in reading the pasted text out of the event, and a later "p" then has
 * something to put back. Nothing is written back to the clipboard.
 * @param {RegisterHandle} register
 * @param {HTMLTextAreaElement} ta
 * @returns {void}
 */
function watchPaste(register, ta) {
    /**
     * @param {ClipboardEvent} event
     * @returns {void}
     */
    const onPaste = (event) => {
        try {
            const data = event.clipboardData;
            if (!data) { return; }
            const text = data.getData("text/plain");
            if (text) { register.adopt(text); }
        } catch { /* ignore */ }
    };
    ta.addEventListener("paste", onPaste);
}


/* ---------- buffer: writing back, and the undo stack behind it ----------
 *
 *  Only .value and the selection are ever touched: nothing is replaced, nothing
 *  around the element is rewritten, so the textarea keeps posting exactly what it
 *  always posted.
 *
 *  execCommand("insertText"/"delete") is deprecated but it is the only way to
 *  produce edits that the browser's own undo stack understands. Without it every
 *  dd would silently poison Ctrl+Z. So the flag kept here says whether the last
 *  edit went through that path, and undo() can tell the truth about what "u" is
 *  able to reach: it answers with the note to show, or null for silence.
 */

/**
 * The textarea's own editing surface.
 * @typedef {object} Buffer
 * @property {(from: number, to: number, text: string) => boolean} replaceRange
 *   replace a range, and say whether the edit reached the browser's undo stack
 * @property {() => void} noteNativeEdit record that the browser made this edit
 *   itself - typing, a paste, its own undo - so the flag is true again
 * @property {() => string | null} undo run the browser's undo, and answer with
 *   the note the statusline should carry, or null
 */

/**
 * @param {HTMLTextAreaElement} ta the textarea to edit
 * @returns {Buffer}
 */
function createBuffer(ta) {
    /* Whether the last edit replaceRange made reached the browser's undo stack.
       It is cleared when the setRangeText fallback had to run instead, so undo()
       can tell the truth about what u is able to reach; any native edit - typing,
       a paste, a browser undo - sets it back, because the browser recorded that
       one itself. */
    /** @type {boolean} */
    let lastApplied = true;

    /**
     * Replace a range, preferring the path the browser's undo stack can see.
     * @param {number} from start of the range to replace, inclusive
     * @param {number} to end of the range, exclusive
     * @param {string} text what goes in its place, "" to delete
     * @returns {boolean} whether the edit reached the browser's undo stack
     */
    function replaceRange(from, to, text) {
        if (from === to && text === "") { return true; }
        ta.focus();
        /** @type {boolean} */
        let applied = false;
        try {
            ta.setSelectionRange(from, to);
            applied = text === ""
                ? Boolean(document.execCommand?.("delete"))
                : Boolean(document.execCommand?.("insertText", false, text));
        } catch {
            applied = false;
        }
        if (!applied) {
            ta.setRangeText(text, from, to, "end");
            try {
                ta.dispatchEvent(new Event("input", { bubbles: true }));
            } catch { /* ignore */ }
        }
        lastApplied = applied;
        return applied;
    }

    /**
     * Note that an edit reached the undo stack by the browser's own route.
     * @returns {void}
     */
    function noteNativeEdit() {
        lastApplied = true;
    }

    /**
     * Undo belongs to the browser, so it can only replay what the browser's own
     * editing commands produced. When the change being undone had to go through
     * setRangeText - because insertText or delete was unavailable or refused -
     * the stack never saw it and a silent u looks like a dead key. So say which
     * happened, in the same slot that echoes a half-typed command; the next
     * keystroke clears it like any other echo.
     * @returns {string | null} what to say, or null when the undo spoke for itself
     */
    function undo() {
        /* Read the flag before the command runs: undo is itself an editing
           command, so its input event would clear the very thing we report on. */
        const reachable = lastApplied;
        /** @type {boolean} */
        let undone = false;
        try { undone = Boolean(document.execCommand?.("undo")); } catch { undone = false; }
        if (reachable) {
            /* An empty stack, or a browser that will not run the command, is
               still worth a word: the key has just done nothing. */
            return undone ? null : "nothing to undo";
        }
        return undone
            ? "undid an older change - the last one missed the undo stack"
            : "cannot undo - the last change missed the undo stack";
    }

    return { replaceRange, noteNativeEdit, undo };
}


/* ---------- statusline: the three spans, and the bar that holds them ----------
 *
 *  An output device and nothing else: it takes the state, the caret and the line
 *  index, and writes three textContent values. No key handler has to know which
 *  data-vim-* attribute a number appears in, and a page that carries only some of
 *  the spans, or none, is not an error.
 *
 *  A page that carries none gets a bar built here. That is the difference from the
 *  first version of the editor, which only ever found spans a page had written: it
 *  now brings its own statusline, so a page needs the form and nothing else.
 */

/**
 * The three places a statusline writes. Each is null when the page does not have
 * it, which is why every write below is guarded.
 * @typedef {object} StatusLine
 * @property {Element | null} mode     where the mode word goes
 * @property {Element | null} buffer   where the echo goes
 * @property {Element | null} position where the line and column go
 */

/**
 * Find the statusline inside the editor's own wrapper.
 * @param {ParentNode} root the nearest [data-vim], or the textarea's parent
 * @returns {StatusLine}
 */
function findStatus(root) {
    return {
        mode: root.querySelector("[data-vim-mode]"),
        buffer: root.querySelector("[data-vim-buffer]"),
        position: root.querySelector("[data-vim-position]"),
    };
}

/**
 * The bar one editor will write to: the page's, if it wrote one, or a new one that
 * belongs to this editor and has not been put anywhere yet.
 *
 * A page whose wrapper already carries any of the three parts keeps full control of
 * where they sit, which is what the recipe on /vim used to be entirely. A page that
 * writes none of them - the usual case now - gets a bar built here and placed by
 * placeStatus(), so the markup a page needs is the form it already had.
 *
 * Returning the bar detached, rather than inserting it here, is what keeps the
 * disclosure promise: the element only reaches the document at the end of a
 * successful attach, so a throw halfway through leaves no empty bar behind, and
 * there is nothing to clean up if it never gets there.
 * @param {ParentNode} root the nearest [data-vim], or the textarea's parent
 * @returns {{ els: StatusLine, bar: HTMLElement | null }} the three slots, and the
 *   bar that has to be placed - null when the page supplied its own
 */
function ensureStatus(root) {
    const supplied = findStatus(root);
    if (supplied.mode !== null || supplied.buffer !== null || supplied.position !== null) {
        return { els: supplied, bar: null };
    }

    const bar = document.createElement("div");
    bar.className = "vim-status";

    /* The same shape the recipe used to ask a page for: one live region holding the
       mode word and the echo, and the position outside it, because a caret count
       read out on every keystroke is noise. */
    const echo = document.createElement("span");
    echo.setAttribute("role", "status");
    const mode = document.createElement("span");
    mode.setAttribute("data-vim-mode", "");
    const buffer = document.createElement("span");
    buffer.setAttribute("data-vim-buffer", "");
    echo.append(mode, buffer);

    const position = document.createElement("span");
    position.setAttribute("data-vim-position", "");
    position.setAttribute("aria-hidden", "true");

    bar.append(echo, position);
    return { els: { mode, buffer, position }, bar };
}

/**
 * Put a bar from ensureStatus() under the editor it belongs to.
 *
 * Under the form rather than under the textarea, so the bar sits outside the box
 * the keys write into: /vim.css gives it border-top: 0 to continue the editor's own
 * border, and /notes.css makes it the last row of the modal's flex column. A
 * textarea with no form around it - or whose form is the wrapper itself - gets the
 * bar immediately after the textarea, which is the same place in a simpler layout.
 * @param {Element} root the editor's wrapper
 * @param {HTMLTextAreaElement} ta
 * @param {HTMLElement | null} bar what ensureStatus() handed back
 * @returns {void}
 */
function placeStatus(root, ta, bar) {
    if (bar === null) { return; }
    const form = ta.closest("form");
    const anchor = form !== null && form !== root && root.contains(form) ? form : ta;
    anchor.insertAdjacentElement("afterend", bar);
}

/**
 * Write the statusline: the mode word, the echo of the half-typed command or the
 * note about the last one, and the line and column of the caret.
 * @param {StatusLine} els
 * @param {VimState} state
 * @param {number} pos the caret offset
 * @param {number[]} starts from lineStarts()
 * @returns {void}
 */
function paintStatus(els, state, pos, starts) {
    const line = lineIndexOf(starts, pos);
    if (els.mode) {
        /* While a prompt is being typed the mode word stands aside, so the colon or
           the slash is the leftmost thing on the line: that is where vim puts the
           prompt, and where the eye goes looking for it. */
        els.mode.textContent = state.prompt !== null
            ? ""
            : (state.mode === "insert" ? "-- INSERT --" : "-- NORMAL --");
    }
    if (els.buffer) {
        els.buffer.textContent = state.prompt !== null
            ? `${state.prompt.kind}${state.prompt.text}`
            : state.buffer;
    }
    if (els.position) { els.position.textContent = `${line + 1}:${pos - starts[line] + 1}`; }
}

/* ---------- motions: where a motion ends, in both of its forms ----------
 *
 *  Every motion answers here twice, from one definition: the exclusive end of the
 *  range it covers - the offset an operator consumes up to, which for $ and l is the
 *  newline rather than a character past it - and the position the caret is allowed to
 *  come to rest on. Both come from motionTarget(), because keeping two
 *  implementations is what let $ and x disagree about where the line ends.
 *
 *  Two motions do not give the same answer twice, and they are the two that have a
 *  neighbouring line to step onto: h and l wrap onto it for the caret and stop at
 *  the line's edge for an operator. That is caretWrap() below, and the reason is in
 *  its comment - a d that quietly took a line break with it would be a delete nobody
 *  asked for.
 *
 *  Nothing here writes to the textarea or paints: a motion is a number, and
 *  the caller decides whether it moves the caret or feeds an operator.
 */

/**
 * The exclusive end of the range a motion covers.
 *
 * h and l stop at the edge of the line here, whatever the caret is allowed to do
 * with them: an operator takes what is on the line it was asked about. The caret's
 * form of those two is caretWrap().
 * @param {string} v the buffer
 * @param {string} key the motion key, e.g. "w" or "$"
 * @param {number} pos where the caret is starting from
 * @param {number} count how many times to repeat the motion, at least 1
 * @returns {number} the exclusive end of the range, which may be the length of
 *   the buffer and so not a position the caret can occupy
 */
function motionTarget(v, key, pos, count) {
    const starts = lineStarts(v);
    const line = lineIndexOf(starts, pos);
    const start = starts[line];
    const end = lineEndOf(v, starts, line);
    let i = pos;

    if (key === "h") { return Math.max(pos - count, start); }
    /* l stops at the newline, so an operator takes count characters and no more,
       and $ stops there too so that d$ leaves the line break alone. */
    if (key === "l") { const l = pos + count; return l < end ? l : end; }
    if (key === "0") { return start; }
    if (key === "^") { return firstNonBlank(v, starts, line); }

    if (key === "$") { return end; }
    if (key === "w") { for (let k = 0; k < count; k += 1) { i = forwardWord(v, i, false); } return i; }
    if (key === "W") { for (let k = 0; k < count; k += 1) { i = forwardWord(v, i, true); } return i; }
    if (key === "b") { for (let k = 0; k < count; k += 1) { i = backWord(v, i, false); } return i; }
    if (key === "B") { for (let k = 0; k < count; k += 1) { i = backWord(v, i, true); } return i; }
    if (key === "e") { for (let k = 0; k < count; k += 1) { i = endWord(v, i, false); } return i; }
    if (key === "E") { for (let k = 0; k < count; k += 1) { i = endWord(v, i, true); } return i; }
    return pos;
}

/**
 * Any offset folded onto a position the caret may actually occupy. The caret
 * never rests on the newline that ends a line, so an offset landing there - $ on
 * a line with characters, l run past the last one, e at the end of the buffer,
 * the first non-blank of a line that is all blanks - steps back onto the last
 * character instead, which is the character it meant. An empty line has no such
 * character, and its start is both its first and its last position.
 *
 * Operators want the other form, the exclusive end of the range, so that d$ keeps
 * the line break and dl takes exactly count characters; that is motionTarget()
 * above, unfurled. Every caret move goes through here, so no motion can leave the
 * caret sitting on a newline where x finds nothing.
 *
 * @param {string} v
 * @param {number[]} starts from lineStarts()
 * @param {number} pos an offset, possibly the length of the buffer
 * @returns {number} a position the caret may occupy
 */
function caretAt(v, starts, pos) {
    const clamped = pos < 0 ? 0 : (pos > v.length ? v.length : pos);
    if (clamped < v.length && v.charAt(clamped) !== "\n") { return clamped; }
    const start = starts[lineIndexOf(starts, clamped)];
    return clamped > start ? clamped - 1 : start;
}

/**
 * The same motion in its inclusive form: where the caret is allowed to come to
 * rest. For everything but h and l that is motionTarget() folded onto a character
 * the caret may occupy; those two wrap onto the neighbouring line, which is
 * caretWrap() below.
 * @param {string} v the buffer
 * @param {number[]} starts from lineStarts() for v
 * @param {string} key one of the motions motionTarget() answers for
 * @param {number} pos where the caret is starting from
 * @param {number} count how many times to repeat the motion, at least 1
 * @returns {number} an offset the caret may actually occupy
 */
function caretTarget(v, starts, key, pos, count) {
    if (key === "h" || key === "l") { return caretWrap(v, starts, key, pos, count); }
    return caretAt(v, starts, motionTarget(v, key, pos, count));
}

/**
 * h and l, the two motions that do not stop at the edge of a line: the caret has a
 * neighbouring line to step onto, so l on the last character of a line lands on the
 * first character of the next, and h on the first character of a line lands on the
 * last character of the one above. A count walks that many characters, over as many
 * line breaks as it takes, and stops when the buffer runs out.
 *
 * vim does this only when 'whichwrap' says so. There are no options here, so it is
 * always on - and what is not optional either is which form wraps: an operator
 * asked for the same motion still stops at the line's edge, because a dl that
 * quietly took the line break with it would be a delete nobody asked for. That is
 * the whole of the difference, and it is why this is not motionTarget(): the caret
 * is allowed to leave its line, a range is not.
 *
 * @param {string} v
 * @param {number[]} starts from lineStarts() for v
 * @param {string} key "h" or "l"
 * @param {number} pos where the caret is starting from
 * @param {number} count how many characters to walk, at least 1
 * @returns {number} a position the caret may occupy
 */
function caretWrap(v, starts, key, pos, count) {
    /* Fold first. A caret the browser left just past the last character of a line -
       a click there, or Escape out of insert mode - is on that character, so the
       first l wraps from it rather than stepping back onto it. */
    let i = caretAt(v, starts, pos);

    for (let k = 0; k < count; k += 1) {
        const line = lineIndexOf(starts, i);
        const start = starts[line];
        const end = lineEndOf(v, starts, line);

        if (key === "l") {
            /* end - 1 is the last character of the line; an empty line has only its
               start, which is its end as well. */
            const last = end > start ? end - 1 : start;
            if (i < last) { i += 1; continue; }
            if (line + 1 >= starts.length) { break; }
            i = starts[line + 1];
            continue;
        }

        if (i > start) { i -= 1; continue; }
        if (line === 0) { break; }
        const aboveStart = starts[line - 1];
        const aboveEnd = lineEndOf(v, starts, line - 1);
        i = aboveEnd > aboveStart ? aboveEnd - 1 : aboveStart;
    }

    return i;
}


/* ---------- insert: getting into insert mode, and out of it again ----------
 *
 *  The six ways in (i, a, I, A, o, O) are one function, because they differ only
 *  in where the caret lands first. The mode changes whichever way the caret went,
 *  so a key that finds nowhere to move still lets you type.
 *
 *  Enter and Tab are here because each is one thing in insert mode and something
 *  else entirely outside it. Enter keeps the line's indentation, which is vim's
 *  'autoindent'; Tab indents and Shift+Tab unindents. In normal mode Enter and Tab
 *  are still the browser's - that is decided in the keydown grammar below, which
 *  never reaches these for them.
 */

/**
 * Enter insert mode the way the key asks for: before the caret (i), after it (a),
 * at the first non-blank (I), at the end of the line (A), or in a new line below
 * (o) or above (O).
 * @param {Editor} ed
 * @param {string} key "i", "a", "I", "A", "o" or "O"
 * @returns {void}
 */
function enterInsert(ed, key) {
    const v = ed.ta.value;
    const starts = lineStarts(v);
    const pos = ed.ta.selectionStart;
    const line = lineIndexOf(starts, pos);
    const start = starts[line];
    const end = lineEndOf(v, starts, line);

    if (key === "a") {
        /* a inserts after the character under the caret, but never past the end
           of the line: the newline is not a character to sit on. */
        ed.setCaret(pos < end ? pos + 1 : end);
    } else if (key === "I") {
        ed.setCaret(firstNonBlank(v, starts, line));
    } else if (key === "A") {
        ed.setCaret(end);
    } else if (key === "o") {
        /* o opens the line below at this line's indentation, so a nested line stays
           nested without retyping the whitespace. The caret goes after the indent,
           ready to type at the depth the line already had. */
        const indent = indentOf(v, starts, line);
        ed.replaceRange(end, end, "\n" + indent);
        ed.setCaret(end + 1 + indent.length);
    } else if (key === "O") {
        const indent = indentOf(v, starts, line);
        ed.replaceRange(start, start, indent + "\n");
        ed.setCaret(start + indent.length);
    } else {
        ed.setCaret(pos);
    }
    ed.state.mode = "insert";
    ed.paint();
}

/**
 * Enter in insert mode: break the line and start the new one at the indentation of
 * the line it left, which is what vim does with 'autoindent' set. Whatever followed
 * the caret follows the indent, exactly as it does in vim: one newline, one indent,
 * and the rest of the line after it.
 *
 * It copies the indentation rather than adding a level, so a tab stays a tab and two
 * spaces stay two, and nothing here needs to be told a width. It is not syntax-aware:
 * a line that opens a template block or a brace does not indent one deeper, because
 * nothing here can tell a brace inside a string from one that opens a block, and a
 * wrong guess costs more than the typing it saves.
 * @param {Editor} ed
 * @returns {void}
 */
function newlineInsert(ed) {
    const v = ed.ta.value;
    const starts = lineStarts(v);
    const line = lineIndexOf(starts, ed.ta.selectionStart);
    const indent = indentOf(v, starts, line);
    /* One edit, so the newline and its indent are one step on the undo stack. */
    ed.replaceRange(ed.ta.selectionStart, ed.ta.selectionEnd, "\n" + indent);
    ed.paint();
}

/**
 * Tab in insert mode is an indent, not a focus move. It replaces the selection
 * when there is one, so selecting text and pressing Tab behaves the way it does
 * in every other editor.
 * @param {Editor} ed
 * @returns {void}
 */
function indentInsert(ed) {
    ed.replaceRange(ed.ta.selectionStart, ed.ta.selectionEnd, "\t");
    ed.paint();
}

/**
 * Shift+Tab walks back over one tab, or up to four spaces, and does nothing at
 * all when there is no indentation to take out.
 * @param {Editor} ed
 * @returns {void}
 */
function unindentInsert(ed) {
    const v = ed.ta.value;
    const pos = ed.ta.selectionStart;
    let start = pos;
    if (start > 0 && v.charAt(start - 1) === "\t") {
        start -= 1;
    } else {
        let spaces = 0;
        while (start > 0 && spaces < 4 && v.charAt(start - 1) === " ") {
            start -= 1;
            spaces += 1;
        }
    }
    if (start === pos) { return; }
    ed.replaceRange(start, pos, "");
    ed.setCaret(start);
}


/* ---------- operators: d, c, y, x, D, C, p, J, and the rest ----------
 *
 *  Every one of them takes the editor handle as its first argument, because they
 *  all need the same four things: the buffer, the register, the caret and the
 *  statusline. The operator grammar that decides which one to run lives in the
 *  keydown grammar below; this section is the doing.
 *
 *  A delete and a change both go through cut(), so the register, the caret and
 *  the statusline cannot disagree about what just happened.
 */

/**
 * Run an operator over a characterwise range: y yanks it, d deletes it, and c
 * deletes it and drops into insert mode. An empty range still changes mode for c,
 * which is what vim does when the motion has nothing left.
 * @param {Editor} ed
 * @param {Operator} op
 * @param {number} start one end of the range
 * @param {number} end the other end; either order
 * @returns {void}
 */
function applyOperator(ed, op, start, end) {
    const v = ed.ta.value;
    const from = Math.max(0, Math.min(start, end));
    const to = Math.min(v.length, Math.max(start, end));
    if (op === "y") {
        if (from < to) { ed.register.take(v.slice(from, to), false); }
        ed.setCaret(from);
        return;
    }
    /* c over an empty range still leaves you in insert mode - that is what vim
       does when the motion has nothing left to take. */
    const emptied = cut(ed, from, to, false);
    if (op === "c") {
        ed.state.mode = "insert";
    } else if (!emptied) {
        ed.paint();
    }
}

/**
 * Run an operator over whole lines: the range is widened to line boundaries and
 * marked linewise in the register, so what p puts back is lines and not a run of
 * characters.
 * @param {Editor} ed
 * @param {Operator} op
 * @param {number} lineA one end line, 0-based
 * @param {number} lineB the other end line, either order
 * @returns {void}
 */
function applyLineOperator(ed, op, lineA, lineB) {
    const v = ed.ta.value;
    const starts = ed.startsFor(v);
    const a = Math.min(lineA, lineB);
    const b = Math.max(lineA, lineB);
    const from = starts[a];
    const to = b + 1 < starts.length ? starts[b + 1] : v.length;
    if (op === "y") {
        if (from < to) { ed.register.take(v.slice(from, to), true); }
        ed.setCaret(from);
        return;
    }
    const emptied = cut(ed, from, to, true);
    if (op === "c") {
        ed.state.mode = "insert";
    } else if (!emptied) {
        ed.paint();
    }
}

/**
 * Takes a range out of the buffer into the register and closes the gap. Every
 * delete and change goes through here, so the register, the caret and the
 * statusline cannot disagree about what just happened. The caret lands on the
 * start of the hole, which is the character that moved in to fill it - the same
 * place vim leaves you.
 * @param {Editor} ed
 * @param {number} from one end of the range
 * @param {number} to the other end; either order
 * @param {boolean} linewise whether the register should hold whole lines
 * @returns {boolean} whether anything was actually taken
 */
function cut(ed, from, to, linewise) {
    const v = ed.ta.value;
    const start = Math.max(0, Math.min(from, to));
    const stop = Math.min(v.length, Math.max(from, to));
    if (start >= stop) { return false; }
    ed.register.take(v.slice(start, stop), linewise);
    ed.replaceRange(start, stop, "");
    ed.setCaret(start);
    return true;
}

/**
 * x and X take count characters forwards or backwards, and never cross a line
 * break: the newline is not a character the caret rests on, so reaching the end
 * of the line means there is nothing left to take.
 * @param {Editor} ed
 * @param {boolean} before X's direction: backwards instead of forwards
 * @param {number} count how many characters to ask for, at least 1
 * @returns {void}
 */
function deleteChars(ed, before, count) {
    const v = ed.ta.value;
    const starts = ed.startsFor(v);
    const pos = ed.ta.selectionStart;
    const line = lineIndexOf(starts, pos);
    if (before) {
        cut(ed, Math.max(starts[line], pos - count), pos, false);
    } else {
        cut(ed, pos, Math.min(lineEndOf(v, starts, line), pos + count), false);
    }
}

/**
 * dd, cc and yy, and Y: the operator aimed at the caret's own line and the
 * count - 1 lines below it.
 * @param {Editor} ed
 * @param {Operator} op
 * @param {number} count how many lines to take, at least 1
 * @returns {void}
 */
function lineOperator(ed, op, count) {
    const starts = ed.startsFor(ed.ta.value);
    const cur = lineIndexOf(starts, ed.ta.selectionStart);
    const last = Math.min(cur + count - 1, starts.length - 1);
    applyLineOperator(ed, op, cur, last);
}

/**
 * Put the register back into the buffer, once per count, at or after the caret.
 * Linewise text lands on a line of its own - below the caret's line for p, above
 * it for P - and characterwise text lands inside the line, just past the
 * character under the caret for p.
 * @param {Editor} ed
 * @param {Register} entry the text to paste, and whether it is whole lines
 * @param {boolean} after true for p, false for P
 * @param {number} count how many copies to paste
 * @returns {void}
 */
function doPaste(ed, entry, after, count) {
    const v = ed.ta.value;
    if (!entry.text) { return; }
    const repeated = entry.text.repeat(count);

    const starts = lineStarts(v);
    const pos = ed.ta.selectionStart;
    const line = lineIndexOf(starts, pos);

    if (entry.linewise) {
        const lineText = repeated.replace(/\n$/, "");
        const nextStart = line + 1 < starts.length ? starts[line + 1] : -1;
        if (nextStart < 0) {
            ed.replaceRange(v.length, v.length, "\n" + lineText);
            ed.setCaret(v.length + 1);
        } else if (after) {
            ed.replaceRange(nextStart, nextStart, lineText + "\n");
            ed.setCaret(nextStart);
        } else {
            ed.replaceRange(starts[line], starts[line], lineText + "\n");
            ed.setCaret(starts[line]);
        }
        return;
    }

    const at = after && pos < v.length && v.charAt(pos) !== "\n"
        ? Math.min(pos + 1, v.length)
        : pos;
    ed.replaceRange(at, at, repeated);
    ed.setCaret(at);
}

/**
 * Paste from the register, or from the clipboard when the register is empty and
 * clipboard-read has already been granted.
 *
 * The clipboard is the only thing here that waits, so the focus is taken back
 * before the text goes in: a permission check must not leave the typing in some
 * other field.
 * @param {Editor} ed
 * @param {boolean} after true for p, false for P
 * @param {number} count how many copies to paste
 * @returns {Promise<void>}
 */
async function startPaste(ed, after, count) {
    if (ed.register.text) {
        doPaste(ed, ed.register, after, count);
        return;
    }
    const text = await ed.register.readClipboard();
    if (!text) { return; }
    ed.register.adopt(text);
    ed.ta.focus();
    doPaste(ed, ed.register, after, count);
}

/**
 * J: pull the next line up onto this one, with a single space where the line
 * break and the joined line's indentation were. Count of them, but never past the
 * end of the buffer.
 * @param {Editor} ed
 * @param {number} count how many line breaks to take out, at least 1
 * @returns {void}
 */
function joinLines(ed, count) {
    let v = ed.ta.value;
    let starts = lineStarts(v);
    const line = lineIndexOf(starts, ed.ta.selectionStart);
    let joined = 1;

    while (joined < count && line + 1 < starts.length) {
        const end = lineEndOf(v, starts, line);
        if (end >= v.length) { break; }
        const nextEnd = line + 2 < starts.length ? starts[line + 2] - 1 : v.length;
        let j = end + 1;
        while (j < nextEnd && (v.charAt(j) === " " || v.charAt(j) === "\t")) { j += 1; }
        ed.replaceRange(end, j, " ");
        v = ed.ta.value;
        starts = lineStarts(v);
        joined += 1;
    }
    ed.paint();
}

/**
 * Go to a line by number, which is what gg with a count and the :N address both
 * ask for. A pending operator takes the jump as a linewise motion instead of
 * moving the caret.
 * @param {Editor} ed
 * @param {number} number the 1-based line number asked for
 * @returns {void}
 */
function gotoLine(ed, number) {
    const v = ed.ta.value;
    const starts = lineStarts(v);
    const target = Math.min(Math.max(number - 1, 0), starts.length - 1);
    if (ed.state.op) {
        applyLineOperator(ed, ed.state.op, lineIndexOf(starts, ed.ta.selectionStart), target);
        return;
    }
    ed.setCaret(starts[target]);
}

/* ---------- search: the pattern, and where it lands ----------
 *
 *  A pattern is a JavaScript regular expression. The browser already has an engine for
 *  one, and a second one here would be a second set of edge cases to keep in step with
 *  nothing. The flags are g, so one scan can find every match at once, and m, so ^ and
 *  $ are the ends of a line rather than the ends of the buffer: a buffer here is lines,
 *  and a vim pattern means what it means on a line. Case is significant, as vim's is
 *  with 'ignorecase' off, and there are no options here to say otherwise.
 *
 *  The last pattern outlives the prompt that typed it, the way it does in vim. It is
 *  not one of the things clearPending() clears, so n and N still know what to repeat
 *  after a dd, and a bare / or ? repeats it in the direction it was opened with.
 *
 *  Nothing is highlighted. A textarea has no way to paint over its own text, and a
 *  mirror element that grew one would be a second buffer to keep in step with this
 *  one. A search therefore moves the caret onto the match, and says on the statusline
 *  when reaching it had to come round an end of the buffer - which is vim's
 *  'wrapscan', always on because there is nobody here to ask.
 */

/**
 * A literal string as a pattern: every character a regular expression would read as
 * syntax is escaped, so a word taken out of the buffer is matched as the text it is.
 * @param {string} text
 * @returns {string}
 */
function escapePattern(text) {
    return text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/**
 * Compile a pattern, or answer null when the engine will not have it. Anything a
 * visitor can type reaches here, a stray bracket included, so a refusal is a value
 * rather than a throw.
 * @param {string} source
 * @returns {RegExp | null}
 */
function compileSearch(source) {
    try {
        return new RegExp(source, "gm");
    } catch {
        return null;
    }
}

/**
 * Every offset the pattern matches at, from one scan of the buffer.
 *
 * A match that has no length is stepped over rather than reported: there is no
 * character for the caret to land on, and advancing past it by hand is also what
 * keeps a pattern like ^ or a* from being an endless loop.
 * @param {RegExp} re a compiled search, whose lastIndex is spent here
 * @param {string} v the buffer
 * @returns {number[]} ascending offsets where a match begins
 */
function searchHits(re, v) {
    /** @type {number[]} */
    const hits = [];
    re.lastIndex = 0;
    let found = re.exec(v);
    while (found !== null) {
        if (found[0].length > 0) { hits.push(found.index); }
        /* exec steps lastIndex itself for a match that has a length; one that has
           none leaves it where it was, so step past that by hand. */
        re.lastIndex = found.index + (found[0].length || 1);
        found = re.lastIndex > v.length ? null : re.exec(v);
    }
    return hits;
}

/**
 * Run a search that is already settled: the pattern, the direction, the offset to
 * start looking at, and how many matches to step over.
 *
 * A match is inclusive at the start, which is what leaves a fresh /word alone when
 * the caret is already on its w. The callers that have to step past the caret - n,
 * N, * and # - do it with the offset they pass, so the rule lives in one place.
 *
 * The count is spent on the list of matches rather than by searching again, so a
 * count typed with a key held down costs one scan and not one scan per repetition.
 *
 * @param {Editor} ed
 * @param {string} pattern the regular expression to find
 * @param {boolean} forward which way to look
 * @param {number} count how many matches to step over, at least 1
 * @param {number} start the offset to begin looking at, inclusive
 * @returns {void}
 */
function runSearch(ed, pattern, forward, count, start) {
    const re = compileSearch(pattern);
    if (re === null) {
        ed.notice(`E54: invalid pattern: ${pattern}`);
        return;
    }
    const hits = searchHits(re, ed.ta.value);
    if (hits.length === 0) {
        ed.notice(`E486: pattern not found: ${pattern}`);
        return;
    }

    /* The first match at or after the start - at or before it, going backwards - or
       the first one in the buffer, which is a wrap. */
    let index = -1;
    if (forward) {
        for (let k = 0; k < hits.length && index < 0; k += 1) {
            if (hits[k] >= start) { index = k; }
        }
    } else {
        for (let k = hits.length - 1; k >= 0 && index < 0; k -= 1) {
            if (hits[k] <= start) { index = k; }
        }
    }
    let wrapped = index < 0;
    if (wrapped) { index = forward ? 0 : hits.length - 1; }

    /* The rest of the count, on the list: step off either end and that is another
       wrap, so a count of one more than there are matches comes back to this one
       and says so. The modulo is applied to a number that may be negative, hence
       the second one. */
    const stepped = index + (forward ? 1 : -1) * (count - 1);
    if (stepped < 0 || stepped >= hits.length) { wrapped = true; }
    index = ((stepped % hits.length) + hits.length) % hits.length;

    ed.setCaret(hits[index]);
    if (wrapped) {
        ed.notice(forward
            ? "search hit BOTTOM, continuing at TOP"
            : "search hit TOP, continuing at BOTTOM");
    }
}

/**
 * The search a prompt ran: what was typed, or the last pattern again when nothing
 * was, which is vim's rule for a bare /. Either way the pattern becomes the one n
 * repeats, and the search starts at the caret.
 * @param {Editor} ed
 * @param {string} text what was typed after the / or ?, possibly ""
 * @param {boolean} forward true for /, false for ?
 * @param {number} count how many matches to step over, at least 1
 * @returns {void}
 */
function runSearchPrompt(ed, text, forward, count) {
    const previous = ed.state.search;
    const pattern = text === "" ? (previous === null ? "" : previous.pattern) : text;
    if (pattern === "") {
        ed.notice("E35: no previous regular expression");
        return;
    }
    ed.state.search = { pattern, forward };
    runSearch(ed, pattern, forward, count, ed.ta.selectionStart);
}

/**
 * n and N: the last pattern again, one character past the caret so that the match
 * the caret is already on is not the answer. n looks the way the last search looked
 * and N the other way, and neither of them makes it a different search: the
 * direction the prompt set is the one n goes on repeating.
 * @param {Editor} ed
 * @param {boolean} reverse true for N, which looks the other way
 * @param {number} count how many matches to step over, at least 1
 * @returns {void}
 */
function repeatSearch(ed, reverse, count) {
    const last = ed.state.search;
    if (last === null) {
        ed.notice("E35: no previous regular expression");
        return;
    }
    const forward = reverse ? !last.forward : last.forward;
    runSearch(ed, last.pattern, forward, count, ed.ta.selectionStart + (forward ? 1 : -1));
}

/**
 * The word the caret is on, by the rule the small-word motions use: a run of
 * characters of one class, so a run of punctuation is a word here too. Empty when the
 * caret is on a blank or past the end of the buffer.
 * @param {string} v the buffer
 * @param {number} pos the caret
 * @returns {string}
 */
function wordAt(v, pos) {
    if (pos < 0 || pos >= v.length) { return ""; }
    const kind = classify(v.charAt(pos), false);
    if (kind === 0) { return ""; }
    let from = pos;
    while (from > 0 && classify(v.charAt(from - 1), false) === kind) { from -= 1; }
    let to = pos;
    while (to + 1 < v.length && classify(v.charAt(to + 1), false) === kind) { to += 1; }
    return v.slice(from, to + 1);
}

/**
 * * and #: search for the word under the caret, forwards or backwards. The word is
 * escaped before it becomes a pattern - a caret on a bracket must not turn into an
 * unmatched group - and the search starts one character past the caret, so the word
 * it is standing on is where this one begins rather than where it lands.
 * @param {Editor} ed
 * @param {boolean} forward true for *, false for #
 * @returns {void}
 */
function searchWord(ed, forward) {
    const word = wordAt(ed.ta.value, ed.ta.selectionStart);
    if (word === "") {
        ed.notice("E348: no string under cursor");
        return;
    }
    const pattern = escapePattern(word);
    ed.state.search = { pattern, forward };
    runSearch(ed, pattern, forward, 1, ed.ta.selectionStart + (forward ? 1 : -1));
}


/* ---------- ex: the command line, and the two commands worth having ----------
 *
 *  ":" opens one prompt on the statusline, Enter runs the ex command typed there
 *  and Escape abandons it. runCommand() is the whole dispatcher - a line address
 *  such as ":42", or ":w" to submit the form that is already on the page - and it
 *  is a prompt rather than a shell: nothing here splits words, so each command
 *  reads the rest of the line as its argument.
 *
 *  The same prompt carries a search: / and ? open it too, and Enter hands the text
 *  to the search section instead of to runCommand(). Only the kind of the prompt
 *  decides which, so a half-typed search is edited and abandoned by the same keys a
 *  half-typed command is.
 *
 *  A command it does not know answers with vim's E492 in the echo, where the next
 *  keystroke clears it like any other note.
 */

/**
 * vim's line grammar, trimmed to the addresses one textarea can answer for: a
 * line number, $ for the last line, . for this one, and a signed offset from this
 * one. Null means "not an address", which is how runCommand tells :42 apart from
 * :w and from a command it does not know.
 * @param {string} text the command line, already trimmed
 * @param {number} current the 0-based line the caret is on
 * @param {number} last the 0-based index of the last line
 * @returns {number | null} a 0-based line index, or null if it is not an address
 *   at all
 */
function lineAddress(text, current, last) {
    if (/^[0-9]+$/.test(text)) {
        return Math.min(Math.max(parseInt(text, 10) - 1, 0), last);
    }
    if (text === "$") { return last; }
    if (text === ".") { return current; }
    const offset = /^([+-])([0-9]*)$/.exec(text);
    if (offset) {
        /* A bare + or - is one line, as it is in vim. */
        const step = offset[2] === "" ? 1 : parseInt(offset[2], 10);
        const moved = current + (offset[1] === "+" ? step : -step);
        return Math.min(Math.max(moved, 0), last);
    }
    return null;
}

/**
 * :w is the Save button, not an imitation of it: the form that is already on the
 * page posts exactly what a click posts, and storage stays the server's business.
 * A textarea outside a form has nowhere to write, which is the E32 here.
 * @param {Editor} ed
 * @returns {void}
 */
function writeForm(ed) {
    const form = ed.ta.form;
    if (!form) {
        ed.notice("E32: nothing to write - the textarea is not in a form");
        return;
    }
    /* The Save button is refused too when the form does not validate, so say that
       rather than claim a write that never left. */
    if (typeof form.checkValidity === "function" && !form.checkValidity()) {
        ed.notice("the form would not validate - nothing written");
        return;
    }
    ed.notice("written");
    /* requestSubmit runs the form the way the button does; submit() is the older
       way out for a browser without it. Ever only one of them. */
    if (typeof form.requestSubmit === "function") { form.requestSubmit(); }
    else { form.submit(); }
}

/**
 * One ex command, taken whole. A line address moves the caret; anything else is
 * reported in the same slot that echoes a half-typed command, where the next
 * keystroke clears it like any other note.
 * @param {Editor} ed
 * @param {string} input the whole command, colon omitted and untrimmed
 * @returns {void}
 */
function runCommand(ed, input) {
    ed.state.prompt = null;
    const command = input.trim();

    /* A bare colon is not an error in vim, and it is not one here. */
    if (command === "") { ed.paint(); return; }

    if (command === "w" || command === "write") { writeForm(ed); return; }

    const v = ed.ta.value;
    const starts = ed.startsFor(v);
    /** @type {number | null} */
    const target = lineAddress(command, lineIndexOf(starts, ed.ta.selectionStart), starts.length - 1);
    if (target !== null) {
        /* A jump lands on the first non-blank of the line, as in vim, which for
           an all-blank line folds back onto a character the caret is allowed to
           rest on. */
        ed.setCaret(caretAt(v, starts, firstNonBlank(v, starts, target)));
        return;
    }

    ed.notice(`E492: not an editor command: ${command}`);
}

/**
 * Every key belongs to the prompt while it is open, so a colon or a slash half typed
 * never edits the buffer. Tab is the one exception: it still moves focus, and leaving
 * the field drops the prompt like any other pending state.
 *
 * Only ever called while a prompt is open, so `state.prompt` is a Prompt here even
 * though it is a `Prompt | null` beside the mode.
 *
 * @param {Editor} ed
 * @param {KeyboardEvent} event
 * @returns {void}
 */
function onPromptKey(ed, event) {
    const state = ed.state;
    const prompt = state.prompt;
    if (prompt === null) { return; }
    const key = event.key;

    if (key === "Tab") { return; }

    if (key === "Escape" || (event.ctrlKey && key === "[")) {
        event.preventDefault();
        state.prompt = null;
        state.buffer = "";
        ed.paint();
        return;
    }

    if (key === "Enter") {
        event.preventDefault();
        state.prompt = null;
        if (prompt.kind === ":") {
            runCommand(ed, prompt.text);
            return;
        }
        /* A search takes no count: a digit typed after the / is part of the pattern,
           which is what it is in vim too. */
        runSearchPrompt(ed, prompt.text, prompt.kind === "/", 1);
        return;
    }

    if (key === "Backspace") {
        event.preventDefault();
        prompt.text = prompt.text.slice(0, -1);
        ed.paint();
        return;
    }

    /* Backspacing a mistyped line one character at a time is no fun. */
    if (event.ctrlKey && key === "u") {
        event.preventDefault();
        prompt.text = "";
        ed.paint();
        return;
    }

    /* Cmd and Alt still belong to the browser, exactly as they do in normal mode,
       so a shortcut never turns into part of a command. */
    if (event.metaKey || event.altKey || event.ctrlKey) { return; }

    if (key.length === 1) {
        event.preventDefault();
        prompt.text += key;
        ed.paint();
        return;
    }

    /* Arrows and friends would move the caret underneath the prompt, which is not
       something a one-line prompt does. */
    event.preventDefault();
}


/* ---------- keys: the whole keyboard, in one handler ----------
 *
 *  The prompt first, then insert mode, then the normal-mode grammar, and
 *  each branch hands off to the code that owns the work. This is the only place
 *  that knows which key means what; nothing it calls looks at a key code.
 *
 *  The platform keeps the keys it owns - Tab and Home and End and PageUp and
 *  PageDown for navigation, Ctrl and Cmd and Alt shortcuts, function keys - so a
 *  keyboard user is never trapped in a field that eats every keystroke. The arrows
 *  are not among them any more: they are h, j, k and l, translated once below and
 *  answered by the grammar those four letters already had. Everything else is
 *  answered here and marked handled, which is what stops a normal-mode "j" from
 *  also moving the caret about like a textarea's.
 */

/**
 * The arrow keys, as the motions they are. One table, read in normal mode only:
 * translating rather than dispatching is what gives j a single implementation and
 * lets ArrowDown inherit every edge case it has - the goal column, a pending
 * operator taking whole lines - instead of a second family of keys that would have
 * to be kept in step with the first.
 * @type {Map<string, string>}
 */
const ARROW_KEYS = new Map([
    ["ArrowLeft", "h"],
    ["ArrowDown", "j"],
    ["ArrowUp", "k"],
    ["ArrowRight", "l"],
]);

/**
 * The whole keyboard, in one handler: the prompt first, then insert mode, then
 * the normal-mode grammar.
 * @param {Editor} ed
 * @param {KeyboardEvent} event
 * @returns {void}
 */
function onKeyDown(ed, event) {
    const ta = ed.ta;
    const state = ed.state;

    if (event.defaultPrevented) { return; }
    if (event.isComposing || event.keyCode === 229) { return; }

    /* Reassignable, because normal mode translates the arrows into motions at the
       head of its own branch: everything after that answers the motion, and the
       prompt and insert mode are decided before it, so they never see one. */
    let key = event.key;

    /* The prompt takes the keyboard first, before insert mode or any of the
       normal-mode grammar gets to see the key. */
    if (state.prompt !== null) {
        onPromptKey(ed, event);
        return;
    }

    if (state.mode === "insert") {
        if (key === "Escape" || (event.ctrlKey && key === "[")) {
            event.preventDefault();
            const here = ta.selectionStart;
            const starts = lineStarts(ta.value);
            const line = lineIndexOf(starts, here);
            state.mode = "normal";
            ed.reset();
            ed.setCaret(here > starts[line] ? here - 1 : here);
            return;
        }
        /* Enter is the one newline the browser's own is not good enough for: the one
           that starts the next line at the same depth. A composing keystroke is the
           IME choosing a candidate rather than a line break, and the modifier
           combinations belong to the platform, so both fall through untouched. */
        if (key === "Enter" && !event.isComposing
            && !event.ctrlKey && !event.altKey && !event.metaKey) {
            event.preventDefault();
            newlineInsert(ed);
            return;
        }
        /* In normal mode Tab is left alone and moves focus, as it should. */
        if (key === "Tab") {
            event.preventDefault();
            if (event.shiftKey) { unindentInsert(ed); } else { indentInsert(ed); }
        }
        return;
    }

    /* normal mode: let the platform keep the keys it owns */
    if (event.metaKey || event.altKey) { return; }
    if (event.ctrlKey && key !== "[") { return; }

    if (key === "Escape" || (event.ctrlKey && key === "[")) {
        event.preventDefault();
        ed.reset();
        return;
    }

    /* The arrows are h, j, k and l, and this is the only place that says so. One
       translation, before the grammar, so the four motions keep one implementation
       and one set of edge cases; everything below answers the motion and not the
       key it arrived on. */
    const arrow = ARROW_KEYS.get(key);
    if (arrow !== undefined) { key = arrow; }

    if (key === "Tab" || key === "Home" || key === "End"
        || key === "PageUp" || key === "PageDown"
        || /^F[0-9]+$/.test(key)) {
        return;
    }

    /* Enter, Backspace and Delete have names longer than one character, so the
       catch-all below used to cancel them and leave them inert. Give them the
       motions they have in vim; a pending operator is not a motion they can
       serve, so that aborts. */
    if (key === "Enter" || key === "Backspace" || key === "Delete") {
        event.preventDefault();
        if (state.op) { ed.reset(); return; }
        const times = pendingCount(state);
        if (key === "Enter") {
            const v = ta.value;
            const starts = lineStarts(v);
            const cur = lineIndexOf(starts, ta.selectionStart);
            const next = Math.min(cur + times, starts.length - 1);
            /* The first non-blank of a line that is all blanks is its end, which
               is the newline: fold it back onto a real character. */
            ed.setCaret(caretAt(v, starts, firstNonBlank(v, starts, next)));
        } else if (key === "Backspace") {
            /* Backspace is h, wrap and all: one key, one meaning, and the caret's
               form of the motion is the one that moves a caret. */
            ed.setCaret(caretTarget(ta.value, ed.startsFor(ta.value), "h", ta.selectionStart, times));
        } else {
            deleteChars(ed, false, times);
        }
        ed.reset();
        return;
    }

    if (key.length !== 1) {
        event.preventDefault();
        return;
    }

    /* The colon opens the command line and the two slashes open a search, rather than
       being echoed as keys of their own, so they have to be caught before the echo
       below starts. Anything pending - a count, an operator, a g prefix - is
       abandoned here: the statusline is about to carry one command and nothing
       else. */
    if (key === ":" || key === "/" || key === "?") {
        event.preventDefault();
        clearPending(state);
        state.prompt = { kind: /** @type {":" | "/" | "?"} */ (key), text: "" };
        ed.paint();
        return;
    }

    event.preventDefault();
    /* A half-typed command goes on echoing itself, but a note is about a command
       that has already finished - the undo warning is the only one - so it must
       not turn into the prefix of the next key pressed. Nothing pending means
       there is nothing in the echo worth keeping. */
    if (state.count === "" && state.op === null && state.prefix === null) { state.buffer = ""; }
    state.buffer += key;
    ed.paint();

    /* counts: a bare 0 is the line-start motion, otherwise a digit */
    if (key >= "0" && key <= "9" && (key !== "0" || state.count !== "")) {
        state.count += key;
        ed.paint();
        return;
    }

    /** @type {number} */
    const count = pendingCount(state);

    if (key === "d" || key === "c" || key === "y") {
        if (state.op === key) { lineOperator(ed, key, count); ed.reset(); return; }
        if (state.op !== null) { ed.reset(); return; }
        state.op = key;
        ed.paint();
        return;
    }

    if (state.prefix === "g") {
        const again = key === "g";
        state.prefix = null;
        if (again) { gotoLine(ed, state.count === "" ? 1 : count); ed.reset(); return; }
        ed.reset();
        return;
    }

    if (key === "g") {
        state.prefix = "g";
        ed.paint();
        return;
    }

    if (key === "j" || key === "k" || key === "G") {
        const v = ta.value;
        const starts = ed.startsFor(v);
        const pos = ta.selectionStart;
        const cur = lineIndexOf(starts, pos);
        /* The column to aim at, which is not always the column the caret is in: a run
           of vertical moves keeps aiming at the one it began at, so stepping down
           over a short line and back up returns to where the run started instead of
           to the end of the long line. The aim is only in force while the caret is
           still where the last vertical move left it, which is what ends the run at
           the first h, word motion, click or edit without any of them saying so. */
        const column = state.aim !== null && state.aim.at === pos
            ? state.aim.column
            : pos - starts[cur];
        /** @type {number} */
        let target;
        if (key === "G") {
            target = state.count === "" ? starts.length - 1 : Math.min(count - 1, starts.length - 1);
        } else {
            target = Math.min(Math.max(cur + (key === "k" ? -count : count), 0), starts.length - 1);
        }
        /* An operator takes lines and leaves the caret somewhere nobody aimed at, so
           the run ends here rather than being picked up again after a dd. */
        if (state.op) {
            state.aim = null;
            applyLineOperator(ed, state.op, cur, target);
            ed.reset();
            return;
        }
        ed.setCaret(caretOnLine(v, starts, target, column));
        /* Recorded after the move, because the guard is the offset the move left:
           that is the position the next j or k has to recognise as its own. */
        state.aim = { column, at: ta.selectionStart };
        ed.reset();
        return;
    }

    if ("hl0^$wbeWBE".includes(key)) {
        const from = ta.selectionStart;
        if (state.op) {
            /* e and E stop on the last character they cover, so an operator has
               to take that character too. Every other motion's target is already
               exclusive: $ and l stop at the newline rather than past it, so d$
               keeps the line break and dl takes exactly count characters. The
               caret gets the inclusive form from caretTarget(), which is the
               whole point of having two. */
            const inclusive = key === "e" || key === "E";
            const to = motionTarget(ta.value, key, from, count) + (inclusive ? 1 : 0);
            applyOperator(ed, state.op, from, to);
            ed.reset();
            return;
        }
        ed.setCaret(caretTarget(ta.value, ed.startsFor(ta.value), key, from, count));
        ed.reset();
        return;
    }

    /* A pending operator is still owed a motion, and the motions above are the
       only ones we have. Anything left over aborts it here rather than running
       the command and quietly forgetting the operator, which is how "d" then "i"
       used to drop into insert mode. */
    if (state.op) { ed.reset(); return; }

    /* Search, after that abort on purpose: n is not a motion an operator can take
       here, so a pending d is cancelled rather than answered by a jump. n and N
       repeat the last pattern; * and # make a pattern out of the word under the
       caret and go looking for the next one. */
    if (key === "n" || key === "N") {
        ed.reset();
        repeatSearch(ed, key === "N", count);
        return;
    }
    if (key === "*" || key === "#") {
        ed.reset();
        searchWord(ed, key === "*");
        return;
    }

    if (key === "x" || key === "X") {
        deleteChars(ed, key === "X", count);
        ed.reset();
        return;
    }
    if (key === "D" || key === "C") {
        const pos = ta.selectionStart;
        const starts = lineStarts(ta.value);
        const line = lineIndexOf(starts, pos);
        applyOperator(ed, key === "D" ? "d" : "c", pos, lineEndOf(ta.value, starts, line));
        ed.reset();
        return;
    }

    if (key === "Y") { lineOperator(ed, "y", count); ed.reset(); return; }

    if ("iIaAoO".includes(key)) { enterInsert(ed, key); ed.reset(); return; }

    if (key === "p" || key === "P") { void startPaste(ed, key === "p", count); ed.reset(); return; }

    if (key === "J") { joinLines(ed, count); ed.reset(); return; }

    if (key === "u") { ed.reset(); ed.undo(); return; }

    /* Anything left over is a key we do not answer to: forget the half-typed
       command so the next one starts clean. */
    ed.reset();
}

/* ---------- editor: one attached textarea, and the handle the rest pass around ----------
 *
 *  attach() in the entry section below delegates here. Nothing is replaced and
 *  nothing around the element is touched: only .value and the selection are read
 *  and written, so the textarea keeps posting exactly what it always posted and
 *  the form stays an ordinary form.
 *
 *  The one thing added to the page is the statusline: built detached by the
 *  statusline section and placed under the form at the end of a successful attach,
 *  where it stays. A page that writes its own bar keeps it, and keeps control of
 *  where it sits.
 *
 *  The editor handle `ed` is the seam between the sections: it carries the
 *  textarea, the command state, the register, the textarea's writing surface and
 *  the three things that touch the statusline - paint, reset and notice. Every
 *  command above takes that handle as its first argument rather than reaching for
 *  anything here, so the dependency runs one way: geometry, motions and operators
 *  are leaves, keys and ex are branches, and this section is the root that knows
 *  about all of them at once.
 *
 *  Every handle this file builds is kept in one registry, which is how a question
 *  about a textarea - is it idle? - is answered from the editor behind it, without
 *  the caller having to have kept the handle it was given.
 */

/**
 * Every handle createEditor() has built, oldest first. A textarea is attached once
 * and stays attached for as long as the page lives, so this is the whole population
 * rather than a list that could disagree with one.
 * @type {Set<VimEditor>}
 */
const liveEditors = new Set();

/**
 * The editor behind one textarea, or null when it carries none.
 *
 * Looked up rather than remembered, so a caller whose idea about whether the editor
 * is attached has gone stale - the page turned it off, say - is told the truth.
 *
 * @param {HTMLTextAreaElement} ta
 * @returns {VimEditor | null}
 */
function editorFor(ta) {
    for (const editor of liveEditors) {
        if (editor.ta === ta) { return editor; }
    }
    return null;
}

/**
 * Everything one attached textarea is, as the other sections see it.
 * @typedef {object} Editor
 * @property {HTMLTextAreaElement} ta the textarea, the only buffer there is
 * @property {VimState} state the command so far
 * @property {StatusLine} els where to write it
 * @property {(value: string) => number[]} startsFor the line index for a value
 * @property {() => void} paint write the statusline from what is there now
 * @property {() => void} reset forget the half-typed command and repaint
 * @property {(text: string) => void} notice say one thing on the statusline
 * @property {(pos: number) => void} setCaret move the caret, collapse the
 *   selection and repaint
 * @property {RegisterHandle} register the unnamed register, and the clipboard
 *   that mirrors it
 * @property {(from: number, to: number, text: string) => boolean} replaceRange
 *   write into the textarea, preferring the browser's undo stack
 * @property {() => void} noteNativeEdit record that the browser made this edit
 *   itself, so the undo stack knows about it
 * @property {() => void} undo run the browser's own undo and say what it reached
 * @property {() => boolean} idle is there nothing half-typed, no prompt open and
 *   no note in the echo?
 */

/**
 * The handle attach() returns. Small on purpose: a caller can ask which mode the
 * editor is in, and everything else stays the textarea's own business.
 *
 * It is also what the registry holds, which is the one reason `ta` is on it: the
 * textarea is how editorFor() finds its way back here.
 * @typedef {object} VimEditor
 * @property {HTMLTextAreaElement} ta the textarea this editor drives
 * @property {() => VimMode} mode the mode the editor is in right now
 * @property {() => boolean} idle is there nothing half-typed, no prompt open and
 *   no note in the echo?
 */

/**
 * Wire modal editing onto one textarea, and say what it did.
 *
 * The work is skipped, rather than repeated, when the textarea is already
 * attached: data-vim-ready is set at the very end of a successful run, so a
 * textarea left half-wired by an error can be tried again.
 *
 * @param {HTMLTextAreaElement | null | undefined} ta the textarea to enhance
 * @returns {VimEditor | null} a handle for it, or null when it declined
 */
function createEditor(ta) {
    if (!ta || ta.getAttribute("data-vim-ready") === "true") { return null; }
    if (typeof ta.setRangeText !== "function") { return null; }

    /** @type {Element | null} */
    const root = ta.closest("[data-vim]") ?? ta.parentElement;
    if (!root) { return null; }

    /* The bar is built now but placed at the very end, with the rest of the
       furniture: until the keys are wired there is nothing worth showing, and a
       throw between here and there must not leave a bar on the page. */
    const status = ensureStatus(root);
    const els = status.els;
    const state = createState();
    const startsFor = createLineIndex();
    const register = createRegister();
    const buffer = createBuffer(ta);

    /**
     * Put the mode on the wrapper, so a stylesheet can colour the caret and the
     * editor's border with it: data-vim-state is "normal" or "insert", and nothing
     * here ever takes it off again.
     *
     * Writing it only when it changes is not a micro-optimisation: paint() runs on
     * every keystroke, and an attribute set to the value it already holds still costs
     * a style recalculation on the subtree it was set on.
     *
     * It is deliberately not called data-vim-mode. That name belongs to the
     * statusline's mode span, and findStatus() looks it up with querySelector: an
     * attribute of that name on the wrapper would be found instead, and painting
     * "-- NORMAL --" into it would overwrite the textarea's own content.
     *
     * @returns {void}
     */
    function paintMode() {
        if (root.getAttribute("data-vim-state") !== state.mode) {
            root.setAttribute("data-vim-state", state.mode);
        }
    }

    /**
     * Write the statusline from the buffer and the caret as they are now, and the
     * mode onto the wrapper while we are here.
     * @returns {void}
     */
    function paint() {
        paintMode();
        paintStatus(els, state, ta.selectionStart, startsFor(ta.value));
    }

    /**
     * Forget the half-typed command. Blur abandons one, and so does every command
     * that ran.
     * @returns {void}
     */
    function reset() {
        clearPending(state);
        paint();
    }

    /**
     * Say one thing in the echo slot, where the next keystroke clears it.
     * @param {string} text
     * @returns {void}
     */
    function notice(text) {
        state.buffer = text;
        paint();
    }

    /**
     * Put the caret at an offset, clamped to the buffer, collapsing any selection.
     * @param {number} pos
     * @returns {void}
     */
    function setCaret(pos) {
        const n = ta.value.length;
        const clamped = pos < 0 ? 0 : (pos > n ? n : pos);
        ta.setSelectionRange(clamped, clamped);
        paint();
    }

    /**
     * Undo, which belongs to the browser. What the buffer reports is the note the
     * statusline should carry when the last change missed the undo stack.
     * @returns {void}
     */
    function undo() {
        const note = buffer.undo();
        if (note !== null) { state.buffer = note; }
        paint();
    }

    /**
     * Is the editor at rest: normal mode, no prompt open, nothing half-typed and no
     * note left in the echo. A search leaves no state of its own behind - only the
     * caret, somewhere it is allowed to be - so a caret sitting on a match is at rest
     * like any other.
     *
     * This is the question a host page actually has. /notes.js closes its dialog on
     * Escape only when the editor has nothing of its own to do with the key, and
     * asking here is the only way to answer that without reading the statusline's
     * text back out of the DOM - which used to mean string-comparing against
     * "-- NORMAL --" and hoping the furniture had been revealed.
     * @returns {boolean}
     */
    function idle() {
        return state.mode === "normal" && state.prompt === null && state.buffer === "";
    }

    /** @type {Editor} */
    const ed = {
        ta,
        state,
        els,
        startsFor,
        paint,
        reset,
        notice,
        setCaret,
        register,
        replaceRange: buffer.replaceRange,
        noteNativeEdit: buffer.noteNativeEdit,
        undo,
        idle,
    };

    /* ---------- wiring ---------- */

    /* The handlers are named rather than passed inline, because handleInput is a
       listener here and also what an input event goes through, and because a named
       handler is what a stack trace can say something about. */
    /**
     * @param {KeyboardEvent} event
     * @returns {void}
     */
    function handleKeyDown(event) { onKeyDown(ed, event); }

    /* A native edit - typing in insert mode, a paste, the browser's own undo - is
       on the undo stack by definition, so it also retires any statusline note
       about an older change that never got there. replaceRange assigns its flag
       after dispatching, so its own setRangeText fallback still wins this. */
    function handleInput() {
        buffer.noteNativeEdit();
        paint();
    }

    ta.addEventListener("keydown", handleKeyDown);
    ta.addEventListener("input", handleInput);
    ta.addEventListener("mouseup", paint);
    ta.addEventListener("keyup", paint);
    ta.addEventListener("focus", paint);
    ta.addEventListener("blur", reset);
    const unwatchPaste = watchPaste(register, ta);

    /* Progressive disclosure: the page sends the plain textarea and keeps whatever
       furniture only makes sense with these keys hidden. Reveal it here, at the
       very end, so a file that fails to parse or throws halfway through leaves the
       no-JS page standing - the failure a <noscript> block cannot cover.
       Document-wide, because a page's own "the keys are attached" line sits outside
       [data-vim]: it is the page's, not any one editor's. The statusline is not in
       this list - it is placed here, so it belongs to the editor that built it. */
    /** @type {NodeListOf<HTMLElement>} */
    const furniture = document.querySelectorAll("[data-vim-only]");
    for (const node of furniture) {
        node.hidden = false;
    }
    placeStatus(root, ta, status.bar);

    paint();

    /* The flag goes on last, on purpose: a throw anywhere above leaves the
       textarea looking unattached, so a later attach() can try again instead of
       being refused by a flag claiming work that never finished. It is also the
       gate that makes a second attach() a no-op rather than a second set of
       listeners on the same textarea. */
    ta.setAttribute("data-vim-ready", "true");

    /* The handle is registered before it is handed out, so that a question about a
       textarea - is it idle? - reaches the editor behind it. Nothing unregisters:
       an editor lives as long as the page does, and a textarea the page throws away
       takes its listeners with it. */
    const handle = { ta, mode() { return state.mode; }, idle };
    liveEditors.add(handle);
    return handle;
}


/* ---------- entry: what a page imports ----------
 *
 *  Importing this file has one side effect, on purpose: it attaches to every
 *  textarea marked for the keys. There is no page-wide question to settle and no
 *  switch to read, because whether a visitor gets these keys was answered before
 *  this file was sent - a page that rendered a plain textarea did not send it.
 *
 *  Nothing below the entry points is exported: no key handler and no internals for
 *  a caller to reach in and take apart. attach, autostart and isIdle are the
 *  module, and the default export is all three.
 */

/**
 * The shape of this module, which is also its default export.
 * @typedef {object} VimModule
 * @property {typeof attach} attach       wire up one textarea
 * @property {typeof autostart} autostart wire up every textarea in a subtree
 * @property {typeof isIdle} isIdle       has this textarea nothing half-typed?
 */

/**
 * Wire up one textarea.
 *
 * The one entry point a page needs for markup that arrived after the module
 * started: nothing here watches the DOM, so a form inserted by a fetch or a
 * template gets an editor from this line and from no other. It is also what
 * autostart() works through.
 *
 * @param {HTMLTextAreaElement | null | undefined} ta the textarea to enhance
 * @returns {VimEditor | null} a handle for it, or null when it declined - no
 *   element, no setRangeText on it, or already attached
 */
export function attach(ta) {
    return createEditor(ta);
}

/**
 * Wire up every textarea marked for the keys, in document order.
 *
 * Both spellings of the mark are looked for, and in one query, so the wrapper and
 * the textarea itself are equally good places for it and no textarea is counted
 * twice.
 *
 * @param {ParentNode} root the subtree to walk: the document, or any part of it
 * @returns {VimEditor[]} one handle per textarea that attached, so a caller can
 *   tell "nothing left to do" from "everything declined"
 */
export function autostart(root = document) {
    /** @type {VimEditor[]} */
    const started = [];
    const marked = root.querySelectorAll("[data-vim] textarea, textarea[data-vim]");
    for (const ta of marked) {
        const editor = attach(/** @type {HTMLTextAreaElement} */ (ta));
        if (editor !== null) { started.push(editor); }
    }
    return started;
}

/**
 * Has this textarea nothing half-typed, no prompt open and no note in the echo? A
 * textarea with no editor at all answers yes, so a page may ask before one
 * attaches.
 *
 * This is the question a host page actually has. /notes.js closes its dialog on
 * Escape only when the editor has nothing of its own to do with the key, and asking
 * here is the only way to answer that without reading the statusline's text back
 * out of the DOM.
 *
 * @param {HTMLTextAreaElement | null | undefined} ta
 * @returns {boolean}
 */
export function isIdle(ta) {
    const editor = ta ? editorFor(ta) : null;
    return editor === null ? true : editor.idle();
}

/**
 * Everything a page can ask for, in one object, for a module that would rather
 * import once than three times.
 * @type {VimModule}
 */
const Vim = {
    attach,
    autostart,
    isIdle,
};

export default Vim;

/* The automatic start: attach to everything marked for the keys. A module script is
   deferred, so the document has usually finished parsing by the time this runs; the
   wait is for the case where it has not, and nothing else depends on it. */
if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", () => { autostart(document); });
} else {
    autostart(document);
}

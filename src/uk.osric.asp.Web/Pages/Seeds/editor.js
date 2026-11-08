/* SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com> */
/* SPDX-License-Identifier: MIT */

const textarea = document.getElementById('template');
const gutter = document.getElementById('gutter');
const editor = document.querySelector('.editor');
const storageKey = `editor:caret:${location.pathname}`;

// The textarea value sanitiser has already replaced CR and CRLF with LF, so LF is the only break.
const lines = () => textarea.value.split('\n').length;

function sync() {
    const wanted = lines();
    while (gutter.childElementCount < wanted) {
        const number = gutter.childElementCount + 1;
        gutter.append(Object.assign(document.createElement('span'),
            { id: `L${number}`, textContent: number }));
    }
    while (gutter.childElementCount > wanted) {
        gutter.lastElementChild.remove();
    }
}

function follow() {
    gutter.scrollTop = textarea.scrollTop;
}

// A diagnostic link is a fragment on a gutter number, so the browser scrolls the gutter alone; the
// textarea has to follow, or the numbers no longer describe the rows they sit beside.
function reveal() {
    const line = Number.parseInt(location.hash.slice(2), 10);
    const number = gutter.children[line - 1];
    if (!number) {
        return;
    }

    textarea.scrollTop = number.getBoundingClientRect().top - gutter.getBoundingClientRect().top
        + gutter.scrollTop;
    follow();
}

function fit() {
    // Clipped to the content box, so no number hides behind the horizontal scrollbar.
    gutter.style.maxHeight = `${textarea.clientHeight}px`;
    // A height the author dragged is the size they asked for; otherwise the viewport decides.
    editor.style.flex = textarea.style.height ? 'none' : '';
}

function remember() {
    try {
        sessionStorage.setItem(storageKey, JSON.stringify({
            start: textarea.selectionStart, end: textarea.selectionEnd, scrollTop: textarea.scrollTop,
        }));
    } catch {
        // Storage can be unavailable (private mode, quota); losing the caret is not worth failing the save.
    }
}

function restore() {
    try {
        const saved = JSON.parse(sessionStorage.getItem(storageKey) ?? 'null');
        if (saved === null) {
            return;
        }

        sessionStorage.removeItem(storageKey);
        const length = textarea.value.length;
        textarea.setSelectionRange(Math.min(saved.start, length), Math.min(saved.end, length));
        textarea.scrollTop = saved.scrollTop;
        textarea.focus();
        follow();
    } catch {
        // A stale or unreadable entry restores nothing.
    }
}

textarea.addEventListener('input', sync);
textarea.addEventListener('scroll', follow, { passive: true });
textarea.form.addEventListener('submit', remember);
addEventListener('hashchange', reveal);
document.querySelector('.diagnostics')?.addEventListener('click', event => {
    if (event.target.closest('a[href^="#L"]')) {
        // After the browser has scrolled to the fragment, so this is the last word on the position.
        setTimeout(reveal);
    }
});
new ResizeObserver(fit).observe(textarea);

sync();
fit();
follow();
restore();
reveal();

export {};

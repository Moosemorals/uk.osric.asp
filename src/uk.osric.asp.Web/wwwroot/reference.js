// SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com>
// SPDX-License-Identifier: MIT

// A type is a <details>, and a link to one of its members has to open it: the browser can scroll to
// a hidden element but cannot reveal it. Open the details that holds the target, on load, on a hash
// change, and when the page's own type index is clicked before the browser handles the hash.
const openTarget = (hash) => {
    if (!hash) {
        return;
    }

    // The id contains a dot, which a CSS selector would read as a class; getElementById does not.
    let id;
    try {
        id = decodeURIComponent(hash.slice(1));
    } catch {
        return;
    }

    // A target can be nested in a type and, later, a member group; every ancestor has to open.
    let node = document.getElementById(id);
    while (node) {
        if (node instanceof HTMLDetailsElement) {
            node.open = true;
        }

        node = node.parentElement;
    }
};

openTarget(location.hash);
addEventListener("hashchange", () => openTarget(location.hash));

for (const link of document.querySelectorAll('.type-index a[href^="#"]')) {
    link.addEventListener("click", () => openTarget(link.hash));
}

export {};

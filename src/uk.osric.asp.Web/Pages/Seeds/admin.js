/* SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com> */
/* SPDX-License-Identifier: MIT */

const boxes = Array.from(document.querySelectorAll('input[name="include"]'));
const counter = document.getElementById('import-count') ?? document.getElementById('export-count');

function plural(total, noun) {
    return total === 1 ? `1 ${noun}` : `${total} ${noun}s`;
}

// The server reads whichever boxes were ticked, so this only keeps the sentence and the button in
// step with what the page shows. Without a script both are still right, if a little stale.
function refresh() {
    if (!counter) {
        return;
    }

    const ticked = boxes.filter(box => box.checked).length;
    counter.textContent = `${plural(ticked, counter.dataset.noun ?? 'page')} selected.`;

    const submit = counter.form?.querySelector('button[type="submit"]');
    if (submit) {
        submit.dataset.label ??= submit.textContent ?? '';
        submit.textContent = `${counter.dataset.verb ?? 'Import'} ${plural(ticked, 'selected page')}`;
    }
}

for (const box of boxes) {
    box.addEventListener('change', refresh);
}

document.getElementById('select-all')?.addEventListener('click', () => {
    for (const box of boxes) {
        box.checked = true;
    }
    refresh();
});

document.getElementById('select-none')?.addEventListener('click', () => {
    for (const box of boxes) {
        box.checked = false;
    }
    refresh();
});

refresh();

// The drop zone is an enhancement over the input it wraps: the file is placed in the input through a
// DataTransfer, so the form posts exactly what choosing a file would have posted.
const zone = document.getElementById('dropzone');
const input = document.getElementById('archive');
const hint = document.getElementById('drop-hint');

if (zone && input) {
    const clear = () => zone.classList.remove('is-dragging');

    zone.addEventListener('dragover', event => {
        event.preventDefault();
        zone.classList.add('is-dragging');
    });

    zone.addEventListener('dragleave', clear);
    zone.addEventListener('drop', event => {
        event.preventDefault();
        clear();

        const file = event.dataTransfer?.files?.[0];
        if (!file) {
            return;
        }

        const transfer = new DataTransfer();
        transfer.items.add(file);
        input.files = transfer.files;
        if (hint) {
            hint.textContent = file.name;
        }
    });
}

export {};

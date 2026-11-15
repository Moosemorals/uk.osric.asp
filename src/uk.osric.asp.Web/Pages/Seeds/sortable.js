/* SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com> */
/* SPDX-License-Identifier: MIT */

// Sorting a table by clicking its header cells.
//
// A table opts in by saying so: class="sortable", exactly one <tbody>, and a
// scope="col" header cell above each column. Importing this module attaches to every
// such table already in the document; rows that arrive later need nothing, because
// the listener is on the table rather than on the rows.
//
// The first click on a column header sorts the rows by that column, the second
// reverses them, and the third puts them back in the order the page sent them - the
// way out of sorting rather than a fourth order. A keyboard visitor does the same by
// tabbing to a header and pressing Enter or Space, which is why each header is given
// a tabindex of 0.
//
// Four rules decide how a column compares, as a class on its header cell:
//
//     sort-alpha              text, trimmed, runs of whitespace collapsed to one
//                             space, compared by code unit - so "Zebra" sorts before
//                             "apple". This is the default and needs no class.
//     sort-alpha-ignore-case  the same, folded to lower case.
//     sort-number             a number. Spaces and commas are furniture and are
//                             dropped, so "1,200" sorts as 1200.
//     sort-date               a date, read by Date.parse; ISO 8601 is the form every
//                             browser reads the same way.
//
// A cell that does not read as a number or a date sorts last whichever way the column
// is going, instead of pretending to be zero or 1970.
//
// A cell may carry data-sort-key, which is compared instead of its text and parsed by
// the same rule. That is what lets a price read "GBP 1,200.00" and sort as 1200, which
// sort-number cannot manage on its own because a currency sign is not furniture.
//
// A column with no order to offer - the actions beside a row - keeps its scope="col",
// because a screen reader needs it to read the table at all, and says data-sort="off"
// instead: no arrow is drawn beside it and clicking it does nothing.
//
// Rows are compared by cell index, not by counting cells from the header's left edge,
// so a body row headed by its own <th scope="row"> lines up with the column above it.
// Rows are moved, never rebuilt: replaceChildren puts them back in one operation, so
// the table is never seen empty and a row's state - a tick, an open <details>, the
// focus - travels with it. Equal keys keep the order the page sent them in both
// directions, so reversing a column of repeated values does not shuffle it.
//
// Left unhandled on purpose: colspan and rowspan in the header row break the index
// alignment, and a table with more than one <tbody> is skipped whole with a warning
// naming it, because guessing which body the headers belong to would be worse than
// not sorting at all.

/**
 * The three states a column can be in. 'none' is unsorted - the order the page sent, which
 * is where a third click returns.
 *
 * @typedef {'ascending' | 'descending' | 'none'} Direction
 */

/**
 * Reads one cell's value into the thing a column compares. A parser is total: a value it
 * cannot read is null, never a throw, and a null sorts last whichever way the column is
 * going. One column's parser returns one type or null, so order() only ever compares like
 * with like.
 *
 * @typedef {(text: string) => string | number | null} Parser
 */

/**
 * What a table remembers between clicks: the direction it is going, the header it is going
 * by, the header it has painted, and where each row sat when it was first seen.
 *
 * @typedef {object} TableRecord
 * @property {WeakMap<HTMLTableRowElement, number>} originals
 *     Each row's place in the order the page sent. A row that arrives later is given the
 *     next number the first time it is sorted.
 * @property {number} next The number to give the next row that appears.
 * @property {HTMLTableCellElement | null} header
 *     The header sorting this table now, or null when it is unsorted.
 * @property {HTMLTableCellElement | null} sorted
 *     The header carrying the painted classes, which is how the last one is reset without
 *     searching the document by class.
 * @property {Direction} direction
 */

/**
 * One row, its key and its place, taken before the sort starts so that comparing two rows
 * does not read the document again.
 *
 * @typedef {object} PreparedRow
 * @property {HTMLTableRowElement} row
 * @property {string | number | null} key Null when the cell's value did not parse.
 * @property {number} position
 */

// One record per table, described by the type above. The field worth a word here is
// originals: it is what a third click restores and what breaks a tie, and a row that
// arrives after attach is given the next number, so it sorts after the rows that were
// there from the start.
/** @type {WeakMap<HTMLTableElement, TableRecord>} */
const records = new WeakMap();

// Attach once per table however often attach() is called: importing the module is a
// side effect, and a page that also calls attach() for rows it wrote itself should not
// end up with two listeners.
/** @type {WeakSet<HTMLTableElement>} */
const attached = new WeakSet();

/** @type {Record<Direction, Direction>} */
const NEXT_DIRECTION = { ascending: 'descending', descending: 'none', none: 'ascending' };

const RUNS_OF_SPACE = /\s+/g;
const NUMBER_FURNITURE = /[\s,]/g;

/**
 * Text as it compares: ends trimmed and each run of whitespace folded to one space, so a
 * cell laid out over several lines is not sorted by its indentation.
 *
 * @param {string} text
 * @returns {string}
 */
function collapsed(text) {
    return text.replace(RUNS_OF_SPACE, ' ').trim();
}

/**
 * The default rule, and the one sort-alpha names: code-unit order, so a capital sorts
 * before a lower-case letter and the answer does not depend on the visitor's locale.
 *
 * @param {string} text
 * @returns {string}
 */
function asText(text) {
    return collapsed(text);
}

/**
 * The sort-alpha-ignore-case rule: the text folded to lower case first, so two values that
 * differ only in case tie and keep the order the page sent them.
 *
 * @param {string} text
 * @returns {string}
 */
function asFoldedText(text) {
    return collapsed(text).toLowerCase();
}

/**
 * The sort-number rule: spaces and commas are furniture and are dropped, then what is left
 * is read whole. Anything Number will not read - '12px', a currency sign, the empty
 * string - is null, and so stays at the bottom of the column.
 *
 * @param {string} text
 * @returns {number | null}
 */
function asNumber(text) {
    const digits = text.replace(NUMBER_FURNITURE, '');
    if (digits === '') {
        return null;
    }
    const value = Number(digits);
    return Number.isFinite(value) ? value : null;
}

/**
 * The sort-date rule: whatever Date.parse understands, as milliseconds since the epoch, so
 * that two dates a year apart compare and a date with a time of day still orders. ISO 8601
 * is the form every browser reads the same way; any other wording is read in the
 * visitor's own time zone, so it can order differently for different visitors.
 *
 * @param {string} text
 * @returns {number | null}
 */
function asDate(text) {
    const trimmed = text.trim();
    if (trimmed === '') {
        return null;
    }
    const value = Date.parse(trimmed);
    return Number.isNaN(value) ? null : value;
}

// Tried in this order. No name here is a prefix of another, so the order is only for
// reading. Every parser is total: a cell it cannot read is null, never a throw.
/** @type {Array<[string, Parser]>} */
const PARSERS = [
    ['sort-alpha-ignore-case', asFoldedText],
    ['sort-number', asNumber],
    ['sort-date', asDate],
    ['sort-alpha', asText],
];

/**
 * The rule a column sorts by, read from the class on its header, defaulting to sort-alpha.
 *
 * @param {HTMLTableCellElement} header
 * @returns {Parser}
 */
function parserFor(header) {
    for (const [name, parse] of PARSERS) {
        if (header.classList.contains(name)) {
            return parse;
        }
    }
    return asText;
}

/**
 * Where a row sat before anything was sorted, inventing the next place for a row that
 * arrived after attach.
 *
 * @param {TableRecord} record
 * @param {HTMLTableRowElement} row
 * @returns {number}
 */
function positionOf(record, row) {
    let known = record.originals.get(row);
    if (known === undefined) {
        known = record.next;
        record.next += 1;
        record.originals.set(row, known);
    }
    return known;
}

/**
 * The value one row offers for one column: data-sort-key where it is written and the cell's
 * text where it is not, both read by the same rule, so the text is a plain reading of the
 * key.
 *
 * @param {HTMLTableRowElement} row
 * @param {number} index The column's cellIndex, which is the header's too.
 * @param {Parser} parse
 * @returns {string | number | null} Null when the value did not parse, and also when the
 *     row is shorter than the header and has no such cell.
 */
function keyOf(row, index, parse) {
    const cell = row.cells[index];
    if (cell === undefined) {
        return null;
    }
    const sortedBy = cell.dataset.sortKey;
    return parse(sortedBy === undefined ? (cell.textContent ?? '') : sortedBy);
}

// The order of two prepared rows. A null key is a cell that did not parse, and it stays
// at the bottom whichever way the column is going; only the values that parsed are
// reversed. Equal keys fall back to the order the page sent them, ascending in both
// directions, so a reverse never shuffles a run of the same value.
/**
 * Compare two prepared rows, for Array.prototype.sort to call.
 *
 * @param {PreparedRow} left
 * @param {PreparedRow} right
 * @param {Direction} direction
 * @returns {number} Negative, zero or positive, as a comparator answers.
 */
function order(left, right, direction) {
    if (direction !== 'none') {
        const first = left.key;
        const second = right.key;
        let sorted = 0;
        if (first === null && second === null) {
            sorted = 0;
        } else if (first === null) {
            sorted = 1;
        } else if (second === null) {
            sorted = -1;
        } else if (first < second) {
            sorted = -1;
        } else if (first > second) {
            sorted = 1;
        }
        if (sorted !== 0) {
            if (first === null || second === null) {
                return sorted;
            }
            return direction === 'descending' ? -sorted : sorted;
        }
    }
    return left.position - right.position;
}

/**
 * Put the body's rows in the order one column asks for, in one operation.
 *
 * @param {HTMLTableElement} table
 * @param {TableRecord} record
 * @param {HTMLTableCellElement} header The column to order by.
 * @param {Direction} direction
 * @returns {void}
 */
function sortRows(table, record, header, direction) {
    const body = table.tBodies[0];
    const index = header.cellIndex;
    const parse = direction === 'none' ? null : parserFor(header);
    const prepared = Array.from(body.rows, (row) => ({
        row,
        key: parse === null ? null : keyOf(row, index, parse),
        position: positionOf(record, row),
    }));
    prepared.sort((left, right) => order(left, right, direction));
    body.replaceChildren(...prepared.map((entry) => entry.row));
}

/**
 * Paint which column this table is sorted by and which way: the classes the sheet draws the
 * arrow from, and the aria-sort a screen reader reads instead of seeing it. Both are
 * written here so they cannot drift apart.
 *
 * @param {TableRecord} record
 * @param {HTMLTableCellElement | null} header The header to mark, null to mark none.
 * @param {Direction} direction
 * @returns {void}
 */
function paintState(record, header, direction) {
    if (record.sorted !== null) {
        record.sorted.classList.remove('sorted', 'reverse');
        record.sorted.setAttribute('aria-sort', 'none');
    }
    record.sorted = direction === 'none' ? null : header;
    if (record.sorted === null) {
        return;
    }
    header.classList.add('sorted');
    header.classList.toggle('reverse', direction === 'descending');
    header.setAttribute('aria-sort', direction);
}

/**
 * The column header a click or a key press belongs to, or null when it belongs to somebody
 * else - a body cell, a row header, a cell of a table nested inside this one, or a control
 * the header happens to hold, because a filter box or a link there is the visitor's. A
 * column marked data-sort="off" has no order to offer, so it is null too.
 *
 * @param {EventTarget | null} target What event.target holds, which can be null and is not
 *     always an Element.
 * @param {HTMLTableElement} table The table whose listener is running; a header belonging
 *     to a table nested inside it is that table's, not this one's.
 * @returns {HTMLTableCellElement | null}
 */
function headerFrom(target, table) {
    if (!(target instanceof Element)) {
        return null;
    }
    if (target.closest('a, button, input, select, textarea, label') !== null) {
        return null;
    }
    const header = target.closest('th');
    if (header === null || header.closest('table') !== table) {
        return null;
    }
    // A row header names its row, so clicking one to sort the first column would be a
    // surprise. Only a column header sorts.
    if (header.getAttribute('scope') !== 'col') {
        return null;
    }
    // A column of links or buttons has no order to offer, and says so where it is written.
    return header.dataset.sort === 'off' ? null : header;
}

/**
 * Sort a table by one column, advancing that column's own cycle: ascending, then
 * descending, then back to the order the page sent. Another column starts ascending.
 *
 * @param {HTMLTableElement} table
 * @param {HTMLTableCellElement} header
 * @returns {void}
 */
function activate(table, header) {
    const record = records.get(table);
    if (record === undefined) {
        return;
    }
    const direction = record.header === header ? NEXT_DIRECTION[record.direction] : 'ascending';
    record.header = direction === 'none' ? null : header;
    record.direction = direction;
    paintState(record, direction === 'none' ? null : header, direction);
    sortRows(table, record, header, direction);
}

/**
 * How a warning names the table it skipped, so a reader can find it: its caption, else its
 * id, else just the class that asked to be sorted.
 *
 * @param {HTMLTableElement} table
 * @returns {string}
 */
function describe(table) {
    const caption = table.caption === null ? '' : collapsed(table.caption.textContent ?? '');
    if (caption !== '') {
        return `the table captioned "${caption}"`;
    }
    if (table.id !== '') {
        return `the table with id "${table.id}"`;
    }
    return 'a table.sortable';
}

/**
 * Attach to one table, or leave it alone and say why in the console when it is one of the
 * shapes this module cannot sort.
 *
 * @param {HTMLTableElement} table
 * @returns {void}
 */
function attachTable(table) {
    if (attached.has(table)) {
        return;
    }
    attached.add(table);
    const bodies = table.tBodies.length;
    if (bodies !== 1) {
        // A second body is usually a heading row or a total, and the header cells and the
        // body cells have to be counted together for the index alignment above to hold,
        // so the table is left alone rather than half sorted.
        console.warn(
            `sortable.js: ${describe(table)} has ${bodies} <tbody> elements rather than one, so it is not sortable.`,
        );
        return;
    }
    records.set(table, { originals: new WeakMap(), next: 0, header: null, sorted: null, direction: 'none' });
    table.addEventListener('click', (event) => {
        const header = headerFrom(event.target, table);
        if (header !== null) {
            activate(table, header);
        }
    });
    // A header is not a button, so the keyboard gets the same treatment by hand: the
    // tabindex makes it reachable and these are the two keys a button would answer to.
    // Space would otherwise scroll the page.
    table.addEventListener('keydown', (event) => {
        if (event.key !== 'Enter' && event.key !== ' ') {
            return;
        }
        const header = headerFrom(event.target, table);
        if (header === null) {
            return;
        }
        event.preventDefault();
        activate(table, header);
    });
    for (const header of table.querySelectorAll('th[scope="col"]:not([data-sort="off"])')) {
        if (header.tabIndex < 0) {
            header.tabIndex = 0;
        }
    }
}

/**
 * Attach to every sortable table under root. Importing the module does this for the whole
 * document; a page that writes its own markup may call it again with something narrower,
 * and a table already attached is left as it is.
 *
 * @param {ParentNode} [root] Where to look, defaulting to the document.
 * @returns {void}
 */
export function attach(root = document) {
    for (const table of root.querySelectorAll('table.sortable')) {
        attachTable(table);
    }
}

// Importing the module is the whole of the usual page's debt: modules are deferred, so
// the document is parsed and every table is here.
attach();

/* SPDX-FileCopyrightText: Copyright 2026 Osric Wilkinson <osric@fluffypeople.com> */
/* SPDX-License-Identifier: MIT */

const root = document.getElementById('assistant-chat');
if (root) {
    const conversationId = root.dataset.conversationId;
    const transcript = document.getElementById('assistant-transcript');
    const statusLine = document.getElementById('assistant-status');
    const optionsForm = document.getElementById('assistant-options-form');
    const optionsStatus = document.getElementById('assistant-options-status');
    const titleInput = document.getElementById('conversation-title');
    const messageForm = document.getElementById('assistant-message-form');
    const sendButton = document.getElementById('assistant-send');
    const stopButton = document.getElementById('assistant-stop');
    const input = document.getElementById('assistant-message');
    const count = document.getElementById('assistant-char-count');
    const messageBudget = document.getElementById('assistant-budget-message');
    const messageMeter = document.getElementById('assistant-budget-message-meter');
    // Keyed by the name the server writes, so an update needs no table of its own.
    const budgetRows = new Map(Array.from(document.querySelectorAll('[data-budget]'), row => [row.dataset.budget, {
        row,
        count: row.querySelector('.assistant-budget-count > span'),
        limit: row.querySelector('.assistant-budget-limit'),
        meter: row.querySelector('meter'),
    }]));
    const token = messageForm.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const messages = new Map();
    // Matches MessageText.MaximumLength, so a streamed tool summary matches the stored one.
    const argumentsLimit = 32;
    const graphemes = typeof Intl.Segmenter === 'function' ? new Intl.Segmenter(undefined, { granularity: 'grapheme' }) : null;
    let source = null;
    let reconnectFailures = 0;

    // The transcript reads newest first, so every new card joins it directly under the prompt box.
    function prepend(node) {
        transcript.insertBefore(node, transcript.firstChild);
    }

    // Mirrors Core's BudgetAmount.Format, so a figure updated live reads the same as the markup it
    // stands in for after a reload: at most three digits and one unit letter.
    function formatAmount(value) {
        if (value < 1000) {
            return String(value);
        }
        const units = ['k', 'M', 'G', 'T', 'P', 'E'];
        let divisor = 1000;
        let unit = 0;
        while (value / divisor >= 1000 && unit < units.length - 1) {
            divisor *= 1000;
            unit += 1;
        }
        const scaled = value / divisor;
        let rounded = scaled < 10 ? Math.round(scaled * 10) / 10 : Math.round(scaled);
        // Rounding 999.6k up would print a fourth digit, so it rolls over to the next unit instead.
        if (rounded >= 1000 && unit < units.length - 1) {
            rounded /= 1000;
            unit += 1;
        }
        return `${rounded}${units[unit]}`;
    }

    function setBusy(busy) {
        input.disabled = busy;
        sendButton.disabled = busy;
        stopButton.hidden = !busy;
        if (busy) {
            root.setAttribute('aria-busy', 'true');
        } else {
            root.removeAttribute('aria-busy');
        }
    }

    function addUserMessage(text) {
        const article = document.createElement('article');
        article.className = 'assistant-message user-message';
        const heading = document.createElement('h2');
        heading.className = 'visually-hidden';
        heading.textContent = 'You';
        const body = document.createElement('pre');
        body.textContent = text;
        article.append(heading, body);
        prepend(article);
    }

    function beginMessage(step) {
        const article = document.createElement('article');
        article.className = 'assistant-message';
        article.setAttribute('aria-busy', 'true');
        const heading = document.createElement('h2');
        heading.className = 'visually-hidden';
        heading.textContent = 'Assistant';
        const reasoning = document.createElement('details');
        reasoning.className = 'assistant-reasoning';
        reasoning.open = true;
        const summary = document.createElement('summary');
        summary.textContent = 'Thinking';
        const reasoningBody = document.createElement('pre');
        reasoning.append(summary, reasoningBody);
        const content = document.createElement('pre');
        content.className = 'assistant-content';
        article.append(heading, reasoning, content);
        // A step can start and then only call tools; it stays out of the transcript until it has text.
        messages.set(step, { article, reasoning, reasoningBody, content, attached: false });
        return messages.get(step);
    }

    function attachMessage(message) {
        if (!message.attached) {
            message.attached = true;
            prepend(message.article);
        }
        return message;
    }

    // A delta is plain text until the step ends, then the server's own restricted renderer turns
    // the stored message into the same HTML the reload will show. If the fetch fails the plain text
    // stays, because losing the answer would be worse than showing it unformatted.
    async function renderCompleted(message, messageId) {
        if (!message || message.rendered || !messageId) {
            return;
        }

        message.rendered = true;
        try {
            const response = await fetch(
                `/assistant/${encodeURIComponent(conversationId)}/messages/${encodeURIComponent(messageId)}/content`,
                { headers: { Accept: 'text/html' } });
            if (!response.ok) {
                return;
            }

            const rendered = document.createElement('div');
            rendered.className = 'assistant-content';
            rendered.innerHTML = await response.text();
            message.content.replaceWith(rendered);
            message.content = rendered;
        } catch {
            // The plain text already in the transcript is the fallback.
        }
    }

    function element(tag, className, text) {
        const created = document.createElement(tag);
        if (className) {
            created.className = className;
        }
        if (text !== undefined) {
            created.textContent = text;
        }
        return created;
    }

    function toolSummary(name, args) {
        const collapsed = String(args ?? '').replace(/\s+/g, ' ').trim();
        const parts = graphemes ? Array.from(graphemes.segment(collapsed), part => part.segment) : Array.from(collapsed);
        const clipped = parts.length > argumentsLimit ? `${parts.slice(0, argumentsLimit - 1).join('')}…` : collapsed;
        return clipped ? `${name ?? 'Tool'} ${clipped}` : String(name ?? 'Tool');
    }

    function buildTool(data) {
        const details = element('details', 'assistant-tool');
        details.dataset.toolCallId = data.toolCallId;
        if (data.ok === false) {
            details.classList.add('assistant-tool-error');
        }
        details.append(element('summary', null, toolSummary(data.name, data.arguments)));
        if (data.arguments) {
            details.append(element('p', 'assistant-tool-label', 'Arguments'), element('pre', 'assistant-tool-arguments', data.arguments));
        }
        if (data.content) {
            details.append(element('p', 'assistant-tool-label', 'Response'), element('pre', 'assistant-tool-response', data.content));
        }
        return details;
    }

    function showTool(data) {
        const existing = transcript.querySelector(`details.assistant-tool[data-tool-call-id="${CSS.escape(data.toolCallId)}"]`);
        const details = buildTool(data);
        if (existing) {
            // A reply replaces the call already in place, so the card keeps the position it joined at.
            details.open = existing.open;
            existing.replaceWith(details);
        } else {
            prepend(details);
        }
    }

    function addApproval(data) {
        const article = document.createElement('article');
        article.className = 'assistant-approval';
        article.dataset.toolCallId = data.toolCallId;
        const heading = document.createElement('h3');
        heading.textContent = data.name;
        const description = document.createElement('p');
        description.textContent = data.description;
        const args = document.createElement('pre');
        args.textContent = data.arguments;
        article.append(heading, description, args);

        for (const [decision, label] of [['approve', 'Approve'], ['deny', 'Deny']]) {
            const form = document.createElement('form');
            form.method = 'post';
            form.action = `/assistant/${encodeURIComponent(conversationId)}/runs/${encodeURIComponent(data.runId)}/tool-calls/${encodeURIComponent(data.toolCallId)}/approval`;
            const csrf = document.createElement('input');
            csrf.type = 'hidden';
            csrf.name = '__RequestVerificationToken';
            csrf.value = token;
            const choice = document.createElement('input');
            choice.type = 'hidden';
            choice.name = 'decision';
            choice.value = decision;
            const button = document.createElement('button');
            button.type = 'submit';
            button.textContent = label;
            form.append(csrf, choice, button);
            form.addEventListener('submit', async event => {
                event.preventDefault();
                button.disabled = true;
                const response = await fetch(form.action, { method: 'POST', body: new FormData(form) });
                if (!response.ok) {
                    button.disabled = false;
                    statusLine.textContent = 'The approval could not be recorded. Reload to check its status.';
                    return;
                }
                statusLine.textContent = decision === 'approve' ? 'Tool approved' : 'Tool denied';
            });
            article.append(form);
        }
        prepend(article);
        statusLine.textContent = 'A tool needs your approval';
    }

    // The server already knows what each figure is; the page only shows it, by the same two rules
    // the markup is written with: the number may exceed the meter, the meter may not. A continuation
    // widens one budget, so the limit the row names moves with the figure.
    function applyBudgets(budgets, limits) {
        for (const [name, used] of Object.entries(budgets ?? {})) {
            const budget = budgetRows.get(name);
            if (!budget) {
                continue;
            }
            const maximum = Number(limits?.[name] ?? budget.meter.max);
            budget.meter.max = String(maximum);
            budget.meter.low = String(Math.floor(maximum * 3 / 5));
            budget.meter.high = String(Math.floor(maximum * 17 / 20));
            budget.meter.value = Math.min(used, maximum);
            budget.count.textContent = formatAmount(used);
            if (budget.limit) {
                budget.limit.textContent = formatAmount(maximum);
            }
            budget.row.classList.toggle('assistant-budget-exceeded', used > maximum);
        }
    }

    // A run that ran out of a budget waits here rather than failing: the same card the server
    // renders on a reload, with the server's own sentence, so the two cannot disagree.
    function addContinuation(data) {
        // A reload renders the server's copy of this card, and the journal replays the event that
        // asked for it, so the live card replaces the stored one rather than joining it.
        transcript.querySelector(`section.assistant-continuation[data-budget="${CSS.escape(data.budget)}"]`)?.remove();
        const section = document.createElement('section');
        section.className = 'assistant-continuation';
        section.dataset.budget = data.budget;
        const heading = document.createElement('h2');
        heading.textContent = 'Do you want to continue?';
        const message = document.createElement('p');
        message.textContent = data.message;
        const form = document.createElement('form');
        form.method = 'post';
        form.action = `/assistant/${encodeURIComponent(conversationId)}/runs/${encodeURIComponent(data.runId)}/continue`;
        const csrf = document.createElement('input');
        csrf.type = 'hidden';
        csrf.name = '__RequestVerificationToken';
        csrf.value = token;
        const button = document.createElement('button');
        button.type = 'submit';
        button.textContent = 'Continue';
        form.append(csrf, button);
        form.addEventListener('submit', async event => {
            event.preventDefault();
            button.disabled = true;
            const response = await fetch(form.action, { method: 'POST', body: new FormData(form) });
            if (!response.ok) {
                button.disabled = false;
                statusLine.textContent = 'The run is no longer waiting to continue. Reload to check its state.';
            }
        });
        const note = document.createElement('p');
        note.className = 'assistant-continuation-note';
        note.textContent = 'Stopping ends the run; the transcript is kept.';
        section.append(heading, message, form, note);
        prepend(section);
        statusLine.textContent = 'Run paused: it needs your answer';
    }

    // The usage list is a different question from the budgets: what the provider reported, not what
    // a limit allows. The totals are already summed by the host, so the page only formats them.
    function applyUsage(usage) {
        if (!usage) {
            return;
        }
        for (const name of ['prompt', 'cached', 'completion', 'reasoning', 'total']) {
            const element = document.getElementById(`assistant-usage-${name}`);
            if (element) {
                element.textContent = formatAmount(usage[name] ?? 0);
            }
        }
        const prompt = usage.prompt ?? 0;
        const cached = usage.cached ?? 0;
        const share = prompt === 0 ? 0 : Math.max(0, Math.min(100, Math.round(100 * cached / prompt)));
        const shareElement = document.getElementById('assistant-usage-cached-share');
        if (shareElement) {
            shareElement.textContent = `(${share}%)`;
        }
    }

    function connect(runId, after) {
        source?.close();
        const url = `/assistant/${encodeURIComponent(conversationId)}/events?run=${encodeURIComponent(runId)}&after=${encodeURIComponent(after)}`;
        source = new EventSource(url);
        setBusy(true);
        statusLine.textContent = 'Assistant is responding';

        source.addEventListener('message.started', event => {
            const data = JSON.parse(event.data);
            beginMessage(data.step);
            reconnectFailures = 0;
        });
        source.addEventListener('reasoning.delta', event => {
            const data = JSON.parse(event.data);
            if (!data.text) {
                return;
            }
            const message = attachMessage(messages.get(data.step) ?? beginMessage(data.step));
            message.reasoningBody.textContent += data.text;
            reconnectFailures = 0;
        });
        source.addEventListener('content.delta', event => {
            const data = JSON.parse(event.data);
            if (!data.text) {
                return;
            }
            const message = attachMessage(messages.get(data.step) ?? beginMessage(data.step));
            message.content.textContent += data.text;
            reconnectFailures = 0;
        });
        source.addEventListener('message.completed', event => {
            const data = JSON.parse(event.data);
            const message = messages.get(data.step);
            if (message) {
                message.article.removeAttribute('aria-busy');
                message.reasoning.open = false;
            }
            void renderCompleted(message, data.messageId);
            statusLine.textContent = 'Response step complete';
        });
        source.addEventListener('budget.updated', event => {
            const data = JSON.parse(event.data);
            applyBudgets(data.budgets, data.limits);
            applyUsage(data.usage);
        });
        source.addEventListener('tool.started', event => showTool(JSON.parse(event.data)));
        source.addEventListener('approval.required', event => addApproval(JSON.parse(event.data)));
        source.addEventListener('approval.resolved', event => {
            const data = JSON.parse(event.data);
            transcript.querySelector(`[data-tool-call-id="${CSS.escape(data.toolCallId)}"].assistant-approval`)?.remove();
            statusLine.textContent = data.decision === 'approved' ? 'Tool approved' : 'Tool denied';
        });
        source.addEventListener('run.paused', event => addContinuation(JSON.parse(event.data)));
        source.addEventListener('run.resumed', event => {
            const data = JSON.parse(event.data);
            transcript.querySelector(`section.assistant-continuation[data-budget="${CSS.escape(data.budget)}"]`)?.remove();
            statusLine.textContent = 'Continuing';
        });
        source.addEventListener('tool.finished', event => {
            const data = JSON.parse(event.data);
            showTool(data);
            statusLine.textContent = data.ok ? 'Tool complete' : 'Tool returned an error';
        });
        source.addEventListener('run.warning', event => {
            const data = JSON.parse(event.data);
            statusLine.textContent = data.message;
        });
        source.addEventListener('run.completed', () => {
            source.close();
            statusLine.textContent = 'Response complete';
            location.reload();
        });
        for (const [terminal, message] of [['run.failed', 'The run stopped'], ['run.cancelled', 'The run was stopped']]) {
            source.addEventListener(terminal, event => {
                const data = JSON.parse(event.data);
                source.close();
                statusLine.textContent = data.message ? `${message}: ${data.message}` : message;
                location.reload();
            });
        }
        source.addEventListener('run.idle', () => {
            source.close();
            setBusy(false);
        });
        source.addEventListener('stream.reset', () => {
            source.close();
            location.reload();
        });
        source.addEventListener('heartbeat', () => {});
        source.onerror = () => {
            reconnectFailures += 1;
            statusLine.textContent = reconnectFailures > 5
                ? 'The stream is unavailable. Reload the page or sign in again.'
                : 'Reconnecting…';
            if (reconnectFailures > 5) {
                source.close();
            }
        };
    }

    function showMessageLength() {
        const used = input.value.length;
        count.textContent = String(used);
        if (messageBudget && messageMeter) {
            messageBudget.textContent = formatAmount(used);
            messageMeter.value = Math.min(used, messageMeter.max);
        }
    }

    // Firefox restores a textarea on reload, so the count is painted once on load as well as on input.
    input.addEventListener('input', showMessageLength);
    showMessageLength();

    // The form still submits as a page for a reader without JavaScript; here it posts and stays.
    optionsForm.addEventListener('submit', async event => {
        event.preventDefault();
        const button = optionsForm.querySelector('button[type="submit"]');
        button.disabled = true;
        try {
            const response = await fetch(optionsForm.action, { method: 'POST', body: new FormData(optionsForm) });
            if (!response.ok) {
                optionsStatus.textContent = response.status === 404
                    ? 'This conversation no longer exists.'
                    : 'The page options could not be saved.';
                return;
            }
            const title = titleInput.value.trim();
            if (title) {
                const heading = document.querySelector('main h1');
                if (heading) {
                    heading.textContent = title;
                }
                document.title = title;
            }
            optionsStatus.textContent = 'Page options saved';
        } catch {
            optionsStatus.textContent = 'The page options could not be saved.';
        } finally {
            button.disabled = false;
        }
    });

    messageForm.addEventListener('submit', async event => {
        event.preventDefault();
        if (event.submitter === stopButton) {
            // A stop needs the token and nothing else; the draft in the box is not part of it.
            const body = new FormData();
            body.append('__RequestVerificationToken', token);
            try {
                await fetch(stopButton.formAction, { method: 'POST', body });
                statusLine.textContent = 'Stopping run';
            } catch {
                // Stopping is what makes the run end, and the run's end is what reloads the page, so
                // the reload can abort this request before it answers. The reload shows the outcome.
            }
            return;
        }
        if (!input.value.trim()) {
            return;
        }
        const content = input.value;
        const response = await fetch(messageForm.action, { method: 'POST', body: new FormData(messageForm) });
        if (response.status !== 202) {
            statusLine.textContent = response.status === 409
                ? 'This conversation already has an active run.'
                : response.status === 429 ? 'The run queue is full. Try again shortly.' : 'The message could not be sent.';
            return;
        }
        const data = await response.json();
        addUserMessage(content);
        input.value = '';
        showMessageLength();
        root.dataset.activeRun = data.runId;
        root.dataset.replayFrom = '0';
        connect(data.runId, 0);
    });

    if (root.dataset.activeRun) {
        connect(root.dataset.activeRun, Number.parseInt(root.dataset.replayFrom ?? '0', 10));
    }
}

export {};

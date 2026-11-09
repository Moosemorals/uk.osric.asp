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
        meter: row.querySelector('meter'),
    }]));
    const token = messageForm.querySelector('input[name="__RequestVerificationToken"]')?.value ?? '';
    const messages = new Map();
    // Matches MessageText.MaximumLength, so a streamed tool summary matches the stored one.
    const argumentsLimit = 32;
    const graphemes = typeof Intl.Segmenter === 'function' ? new Intl.Segmenter(undefined, { granularity: 'grapheme' }) : null;
    let source = null;
    let reconnectFailures = 0;

    function nearBottom() {
        return document.documentElement.scrollHeight - window.scrollY - window.innerHeight < 120;
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

    function keepNearBottom(wasNear) {
        if (wasNear) {
            window.scrollTo(0, document.documentElement.scrollHeight);
        }
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
        const wasNear = nearBottom();
        const article = document.createElement('article');
        article.className = 'assistant-message user-message';
        const heading = document.createElement('h2');
        heading.className = 'visually-hidden';
        heading.textContent = 'You';
        const body = document.createElement('pre');
        body.textContent = text;
        article.append(heading, body);
        transcript.append(article);
        keepNearBottom(wasNear);
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
            transcript.append(message.article);
        }
        return message;
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
        const wasNear = nearBottom();
        const existing = transcript.querySelector(`details.assistant-tool[data-tool-call-id="${CSS.escape(data.toolCallId)}"]`);
        const details = buildTool(data);
        if (existing) {
            details.open = existing.open;
            existing.replaceWith(details);
        } else {
            transcript.append(details);
        }
        keepNearBottom(wasNear);
    }

    function addApproval(data) {
        const wasNear = nearBottom();
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
        transcript.append(article);
        keepNearBottom(wasNear);
        statusLine.textContent = 'A tool needs your approval';
    }

    // The server already knows what each figure is; the page only shows it, by the same two rules
    // the markup is written with: the number may exceed the meter, the meter may not.
    function applyBudgets(budgets) {
        for (const [name, used] of Object.entries(budgets ?? {})) {
            const budget = budgetRows.get(name);
            if (!budget) {
                continue;
            }
            const maximum = Number(budget.meter.max);
            budget.count.textContent = formatAmount(used);
            budget.meter.value = Math.min(used, maximum);
            budget.row.classList.toggle('assistant-budget-exceeded', used > maximum);
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
            const wasNear = nearBottom();
            const message = attachMessage(messages.get(data.step) ?? beginMessage(data.step));
            message.reasoningBody.textContent += data.text;
            keepNearBottom(wasNear);
            reconnectFailures = 0;
        });
        source.addEventListener('content.delta', event => {
            const data = JSON.parse(event.data);
            if (!data.text) {
                return;
            }
            const wasNear = nearBottom();
            const message = attachMessage(messages.get(data.step) ?? beginMessage(data.step));
            message.content.textContent += data.text;
            keepNearBottom(wasNear);
            reconnectFailures = 0;
        });
        source.addEventListener('message.completed', event => {
            const data = JSON.parse(event.data);
            const message = messages.get(data.step);
            if (message) {
                message.article.removeAttribute('aria-busy');
                message.reasoning.open = false;
            }
            statusLine.textContent = 'Response step complete';
        });
        source.addEventListener('budget.updated', event => applyBudgets(JSON.parse(event.data).budgets));
        source.addEventListener('tool.started', event => showTool(JSON.parse(event.data)));
        source.addEventListener('approval.required', event => addApproval(JSON.parse(event.data)));
        source.addEventListener('approval.resolved', event => {
            const data = JSON.parse(event.data);
            transcript.querySelector(`[data-tool-call-id="${CSS.escape(data.toolCallId)}"].assistant-approval`)?.remove();
            statusLine.textContent = data.decision === 'approved' ? 'Tool approved' : 'Tool denied';
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
            await fetch(stopButton.formAction, { method: 'POST', body });
            statusLine.textContent = 'Stopping run';
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

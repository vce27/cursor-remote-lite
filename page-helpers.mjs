// Injected into Cursor's renderer via Function.prototype.toString (CDP Runtime.evaluate).
// Keep as a plain function — helpersCall stringifies it.
export function pageHelpers() {
  const vis = e => { const b = e.getBoundingClientRect(); return b.width > 0 && b.height > 0; };
  const esc = s => s.replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
  const SKIP = /ui-collapsible-header|md-clickable-code|output-preview|codicon-find|widget-close|model-picker/;
  const ACTION = /^(run|skip|allow|accept|reject|deny|approve|keep|undo|continue|stop|cancel|build|retry|yes|no\b|运行|跳过|允许|接受|拒绝|继续|停止|取消)/i;
  const BLOCK = new Set(['P', 'UL', 'OL', 'LI', 'H1', 'H2', 'H3', 'H4', 'H5', 'BLOCKQUOTE', 'TABLE', 'THEAD', 'TBODY', 'TR', 'TD', 'TH', 'DIV']);
  const INLINE = new Set(['STRONG', 'B', 'EM', 'I', 'CODE', 'DEL']);
  const CODE_BLOCK = '.composer-code-block-container, .composer-message-codeblock, .ui-code-block, pre';
  const FILE_REF = 'code.md-inline-path-filename-like';

  // Edit/read tool cards keep the absolute path in the header's title; fall back to the shown file name.
  const toolFile = el => {
    const titled = [...el.querySelectorAll('[title]')].map(e => e.getAttribute('title')).find(t => /^(\/|~\/|[A-Za-z]:\\)\S+$/.test(t));
    return titled || el.querySelector('.ui-edit-tool-call__filename')?.innerText.trim() || null;
  };

  // Classic editor chats use data-flat-index; Agents / glass UI uses data-message-kind (no flat index).
  const ITEM = '[data-flat-index], [data-message-kind]';
  const itemId = el => el.dataset.flatIndex || el.dataset.messageId || el.dataset.messageIndex || '';
  const inItem = el => el.closest(ITEM);
  const findPanel = () => {
    // Agents keeps multiple conversation shells in the DOM; pick the one with the focused
    // composer, otherwise the visible transcript that currently holds the most messages.
    const shells = [...document.querySelectorAll('.composer-messages-container, .composer-react-transcript-root, .agent-panel-conversation-shell')]
      .filter(vis)
      .map(el => ({ el, n: el.querySelectorAll(ITEM).length }));
    const focused = document.activeElement?.closest?.('.aislash-editor-input, .ui-prompt-input-editor__input, [contenteditable="true"]');
    const fromFocus = focused && shells.map(s => s.el).find(el => el.contains(focused));
    if (fromFocus) return fromFocus;
    shells.sort((a, b) => b.n - a.n);
    if (shells[0]?.n) return shells[0].el;

    // Classic: climb from a visible composer until an ancestor contains message items.
    // Prefer the climb that sees the most items (ghost/readonly editors can match first otherwise).
    const ranked = [...document.querySelectorAll('.aislash-editor-input, .ui-prompt-input-editor__input')].filter(vis)
      .map(ed => {
        let p = ed;
        while (p && !p.querySelector(ITEM)) p = p.parentElement;
        return p ? { p, n: p.querySelectorAll(ITEM).length } : null;
      })
      .filter(Boolean)
      .sort((a, b) => b.n - a.n);
    if (ranked[0]) return ranked[0].p;

    const f = [...document.querySelectorAll(ITEM)].find(vis);
    if (!f) return null;
    return f.closest('.composer-messages-container, .composer-react-transcript-root, .composer-bar, .agent-panel-conversation-shell, .agent-conversation-composer__viewport') || f.parentElement;
  };

  const buttonsIn = (root, strict) => [...root.querySelectorAll('button, [role=button], .anysphere-button, .anysphere-secondary-button, .composer-run-button, .composer-skip-button')]
    .filter(b => vis(b) && !SKIP.test(b.className) && !b.parentElement.closest('button, [role=button]'))
    .map(b => ({ el: b, label: ((b.innerText || '').trim() || b.getAttribute('aria-label') || b.title || '').split('\n')[0].slice(0, 30) }))
    .filter(x => x.label && x.label !== 'Shell command options' && (!strict || ACTION.test(x.label)));

  const codeText = n => {
    const lines = n.querySelectorAll('.view-line');
    return lines.length ? [...lines].map(l => l.innerText).join('\n') : n.innerText;
  };

  const clean = node => {
    let out = '';
    for (const n of node.childNodes) {
      if (n.nodeType === 3) { out += esc(n.textContent); continue; }
      if (n.nodeType !== 1) continue;
      if (n.matches(CODE_BLOCK)) { out += `<pre>${esc(codeText(n).replace(/\n$/, ''))}</pre>`; continue; }
      const tag = n.tagName;
      if (['STYLE', 'SCRIPT', 'BUTTON', 'svg', 'SVG', 'IMG'].includes(tag)) continue;
      if (tag === 'BR') { out += '<br>'; continue; }
      if (tag === 'HR') { out += '<hr>'; continue; }
      if (n.matches(FILE_REF)) { out += `<code data-file="${esc(n.innerText.trim())}">${esc(n.innerText.trim())}</code>`; continue; }
      const inner = clean(n);
      const t = tag.toLowerCase();
      out += BLOCK.has(tag) || INLINE.has(tag) ? `<${t}>${inner}</${t}>` : inner;
    }
    return out;
  };

  const summary = el => {
    const head = el.querySelector('.ui-tool-call-card__header, .ui-collapsible-header, [class*="header"]') || el;
    return head.innerText.trim().replace(/\s*\n\s*/g, ' · ').slice(0, 240);
  };

  const topItems = panel => {
    const flats = [...panel.querySelectorAll('[data-flat-index]')].filter(e => !e.parentElement.closest('[data-flat-index]'));
    if (flats.length) return flats;
    return [...panel.querySelectorAll('[data-message-kind]')].filter(e => !e.parentElement.closest('[data-message-kind]'));
  };

  // Messages sent while the Agent is running wait in Cursor's queue tray until the current turn ends.
  const QUEUE_ROW = '[data-queue-row], .composer-toolbar-queue-item[data-queue-item-id]';
  const queueRows = () => [...document.querySelectorAll(QUEUE_ROW)].filter(vis);
  const queued = () => queueRows().map(e => ({
    id: e.dataset.queueItemId,
    t: (e.dataset.queueItemQuery || e.innerText || '').trim().slice(0, 4000),
  }));
  const isBusy = () => [...document.querySelectorAll('.composer-button-area .codicon-debug-stop, .send-with-mode .codicon-debug-stop')].some(vis);

  // First call (no action) returns the row center to hover over; hover-only buttons appear after that.
  const queueTarget = (id, action) => {
    const row = queueRows().find(e => e.dataset.queueItemId === id);
    if (!row) return null;
    row.scrollIntoView({ block: 'nearest' });
    if (!action) return center(row);
    const b = row.querySelector(`[data-queue-action="${CSS.escape(action)}"]`)
      || [...row.querySelectorAll('[aria-label]')].find(e => e.getAttribute('aria-label') === { send: 'Send now', remove: 'Remove', edit: 'Edit' }[action]);
    return b && vis(b) ? center(b) : null;
  };

  // Running subagents: task cards in the transcript plus the "N subagents running" tray above the composer.
  // Clicking a tray row opens that subagent's own transcript as a chat-editor tab (no composer there).
  const SUB_ROW = '.composer-toolbar-background-job-item';
  const subRowName = e => e.querySelector('.composer-toolbar-background-job-item-text')?.innerText.trim() || '';
  const subagents = () => {
    const names = new Set();
    for (const h of document.querySelectorAll('.task-tool-call-header')) {
      if (!vis(h) || !inItem(h)?.querySelector('.ui-subagent-status-indicator--running-loader, .task-subagent-header-pill-button--stop')) continue;
      const n = h.querySelector('span.truncate')?.childNodes[0]?.textContent.trim();
      if (n) names.add(n);
    }
    for (const r of document.querySelectorAll(SUB_ROW)) if (subRowName(r)) names.add(subRowName(r));
    return [...names];
  };
  const subTrayHeader = () => [...document.querySelectorAll('div')].find(e => vis(e) && [...e.childNodes].some(n => n.nodeType === 3 && /^\d+ subagents? running$/.test(n.textContent.trim())));
  const subTarget = (name, part) => {
    const row = [...document.querySelectorAll(SUB_ROW)].find(r => vis(r) && subRowName(r) === name);
    if (!row) { const h = subTrayHeader(); return h ? { expand: true, ...center(h) } : null; }
    if (part === 'stop') {
      const s = [...row.querySelectorAll('span.truncate')].find(e => e.innerText.trim() === 'Stop');
      return s ? center(s) : null;
    }
    return center(row.querySelector('.composer-toolbar-background-job-item-text') || row);
  };
  const activeChatTab = () => [...document.querySelectorAll('.tab.active')].map(e => e.getAttribute('aria-label') || '').find(l => /Chat Editors/.test(l)) || null;
  const tabTarget = label => {
    const t = [...document.querySelectorAll('.tab')].find(e => vis(e) && e.getAttribute('aria-label') === label);
    return t ? center(t) : null;
  };
  const hasComposer = () => [...document.querySelectorAll('.aislash-editor-input[contenteditable="true"], .ui-prompt-input-editor__input[contenteditable="true"]')].some(vis);

  // The Agent's ask-question tool renders as a toolbar above the composer, outside the message list.
  const QN = '.composer-questionnaire-toolbar';
  const qnBox = () => [...document.querySelectorAll(QN)].find(vis);
  const inQn = el => !!el.closest(QN);
  const qnOptions = q => [...q.querySelectorAll('.composer-questionnaire-toolbar-option')];
  const qnAction = (box, re) => [...box.querySelectorAll('.composer-questionnaire-toolbar-actions > *')].find(e => vis(e) && re.test(e.innerText));
  const questions = () => {
    const box = qnBox();
    if (!box) return null;
    const list = [...box.querySelectorAll('.composer-questionnaire-toolbar-question')].map(q => ({
      text: (q.querySelector('.composer-questionnaire-toolbar-question-label .markdown-root') || q.querySelector('.composer-questionnaire-toolbar-question-label'))?.innerText.trim() || '',
      options: qnOptions(q).map(o => {
        const ta = o.querySelector('textarea');
        return {
          label: ta ? '' : o.querySelector('.composer-questionnaire-toolbar-option-label')?.innerText.trim() || '',
          free: !!ta, value: ta?.value || '',
          sel: !!o.querySelector('.composer-questionnaire-toolbar-option-letter-selected'),
        };
      }),
    }));
    const submit = qnAction(box, /continue|submit|继续|提交/i);
    return { list, canSubmit: !!submit && submit.dataset.disabled !== 'true' };
  };
  const questionTarget = (qi, oi, part) => {
    const box = qnBox();
    if (!box) return null;
    if (part === 'submit' || part === 'skip') {
      const b = qnAction(box, part === 'skip' ? /skip|跳过/i : /continue|submit|继续|提交/i);
      return b ? center(b) : null;
    }
    const q = box.querySelectorAll('.composer-questionnaire-toolbar-question')[qi];
    const o = q && qnOptions(q)[oi];
    if (!o) return null;
    o.scrollIntoView({ block: 'nearest' });
    return center(o.querySelector('textarea') || o);
  };

  // Files changed by the Agent: the "N Files" toggle above the composer expands a list with per-file undo / keep.
  const filesToggle = () => [...document.querySelectorAll('span.cursor-pointer')].find(e => vis(e) && /^\d+ Files?$/.test(e.innerText.trim().replace(/\s+/g, ' ')));
  const changedCount = () => parseInt(filesToggle()?.innerText || '0', 10) || 0;
  const changedRows = () => [...document.querySelectorAll('.composer-file-list-item')].filter(vis);
  const changedFiles = () => changedRows().map(r => {
    const stat = [...r.querySelectorAll('.tabular-nums span')].map(s => s.innerText.trim());
    return {
      name: r.querySelector('span[style*="nowrap"]')?.innerText.trim() || r.innerText.trim().split('\n')[0],
      add: stat.find(s => s.startsWith('+')) || '',
      del: stat.find(s => /^[-−]/.test(s)) || '',
    };
  });
  const filesBar = () => {
    let t = filesToggle();
    while (t && !t.querySelector('[data-click-ready]')) t = t.parentElement;
    return t;
  };
  // Bar-level actions besides Stop (handled elsewhere) and Review (desktop-only diff view), e.g. Keep All / Undo All.
  const fileBarButtons = () => [...(filesBar()?.querySelectorAll('[data-click-ready]') || [])].filter(vis)
    .map(e => ({ el: e, label: (e.querySelector('.truncate') || e).innerText.trim() }))
    .filter(x => x.label && !/^(stop|review)$/i.test(x.label));
  const filesState = () => ({ files: changedFiles(), actions: fileBarButtons().map(x => x.label) });
  const fileTarget = (i, name, act) => {
    if (act === 'toggle') { const t = filesToggle(); return t ? center(t) : null; }
    if (act === 'bar') return (b => b ? center(b.el) : null)(fileBarButtons().find(x => x.label === name));
    const row = changedRows()[i];
    if (!row || changedFiles()[i].name !== name) return null;
    row.scrollIntoView({ block: 'nearest' });
    if (act === 'row') return center(row);
    const icon = row.querySelector(act === 'undo' ? '.codicon-x-two' : '.codicon-check-two');
    return icon ? center(icon.closest('.anysphere-icon-button') || icon) : null;
  };

  // Editing a queued message loads it into the main composer, which is tagged with the item id until saved.
  const focusQueueEdit = id => {
    const bar = [...document.querySelectorAll('[data-editing-queue-item-id]')].find(e => vis(e) && e.dataset.editingQueueItemId === id);
    const ed = bar && [...bar.querySelectorAll('[contenteditable="true"]')].find(vis);
    if (!ed) return false;
    ed.focus();
    return true;
  };

  const stopTarget = () => {
    const icon = [...document.querySelectorAll('.composer-button-area .codicon-debug-stop, .send-with-mode .codicon-debug-stop')].find(vis);
    if (!icon) return null;
    return center(icon.closest('button, [role=button], .anysphere-icon-button') || icon);
  };

  const extract = () => {
    const panel = findPanel();
    if (!panel) return null;
    const r = panel.getBoundingClientRect();
    const items = topItems(panel).map(el => {
      const kindEl = el.matches('[data-message-kind]') ? el : el.querySelector('[data-message-kind]');
      const k = kindEl?.dataset.messageKind || (el.querySelector('.composer-human-message, .aislash-editor-input-readonly') ? 'human' : 'other');
      const item = { i: itemId(el), k };
      if (k === 'human') item.t = (el.querySelector('.aislash-editor-input-readonly, .composer-human-message') || el).innerText.trim();
      else if (k === 'assistant') { const md = el.querySelector('.markdown-root'); item.h = md ? clean(md) : esc(el.innerText); }
      else item.t = summary(el);
      if (k === 'tool') { const f = toolFile(el); if (f) item.f = f; }
      const b = buttonsIn(el, false);
      if (b.length) item.b = b.map(x => x.label);
      return item;
    });
    const global = buttonsIn(panel, true).filter(x => !inItem(x.el) && !inQn(x.el)).map(x => x.label);
    const modeEl = [...document.querySelectorAll('.composer-unified-dropdown[data-mode]')].find(vis);
    const modelEl = [...document.querySelectorAll('.ui-model-picker__trigger, .composer-unified-dropdown-model')].find(vis);
    return {
      rect: [r.x, r.y, r.width, r.height].map(Math.round),
      items,
      global,
      busy: isBusy(),
      queued: queued(),
      subagents: subagents(),
      changed: changedCount(),
      questions: questions(),
      readonly: !hasComposer(),
      mode: modeEl ? modeEl.innerText.trim() || modeEl.dataset.mode : null,
      model: modelEl ? modelEl.innerText.trim() : null,
    };
  };

  // Named controls in Cursor's UI; each entry lists fallbacks from newest to oldest Cursor layouts.
  const CONTROLS = {
    mode: () => [...document.querySelectorAll('.composer-unified-dropdown[data-mode]')].find(vis),
    model: () => [...document.querySelectorAll('.ui-model-picker__trigger, .composer-unified-dropdown-model')].find(vis),
    history: () => [...document.querySelectorAll('[aria-label^="Show Chat History"], [aria-label^="Show Agent History"]')].find(vis),
    newChat: () => [...document.querySelectorAll('[aria-label^="New Agent"], [aria-label^="New Chat"]')].find(vis),
  };

  const center = el => {
    const r = el.getBoundingClientRect();
    return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
  };

  const control = name => {
    const el = CONTROLS[name]?.();
    return el ? center(el) : null;
  };

  const MENU_ITEMS = {
    mode: () => [...document.querySelectorAll('.composer-unified-context-menu-item')].map(e => ({ el: e, label: e.innerText.trim().split('\n')[0], checked: !!e.querySelector('.codicon-check') })),
    model: () => [...document.querySelectorAll('.ui-menu__row')].filter(e => e.querySelector('.ui-model-picker__item-content-name')).map(e => ({ el: e, id: e.dataset.testid, params: !!e.querySelector('[data-testid=parameter-edit-btn]'), label: e.querySelector('.ui-model-picker__item-content-name').innerText.trim(), checked: !!e.querySelector('.codicon-check, .ui-model-picker__item-right-section [class*=check]') })),
    history: () => {
      const rows = [...document.querySelectorAll('.composer-history-hover-menu .ui-menu__row')];
      if (rows.length) return rows.map(e => ({ el: e, label: (e.querySelector('.compact-agent-history-react-menu-label, .ui-menu__item-content') || e).innerText.trim().split('\n')[0].slice(0, 80), checked: e.getAttribute('aria-checked') === 'true' || /\b(selected|active|current)\b/.test(e.className) }));
      return [...document.querySelectorAll('.quick-input-widget .monaco-list-row')].map(e => ({ el: e, label: (e.getAttribute('aria-label') || e.innerText).trim().split('\n')[0].slice(0, 80), checked: e.classList.contains('focused') }));
    },
  };

  const menuItems = kind => (MENU_ITEMS[kind]?.() || []).filter(x => vis(x.el) && x.label).map(({ el, ...rest }) => rest);

  const menuOpen = () => [...document.querySelectorAll('.ui-menu, .composer-unified-context-menu-item, .composer-history-hover-menu')].some(vis);

  // Model parameters (context size, effort, fast…) live in a per-model submenu behind its "Edit" button.
  const maxToggle = () => {
    const t = [...document.querySelectorAll('[data-testid=max-mode-toggle]')].find(vis);
    return t ? { ...center(t), on: t.getAttribute('aria-checked') === 'true' } : null;
  };
  const modelTarget = (id, part) => {
    const row = [...document.querySelectorAll('[data-testid=model-picker-menu] .ui-menu__row')].find(e => e.dataset.testid === id);
    if (!row) return null;
    row.scrollIntoView({ block: 'nearest' });
    if (part !== 'edit') return center(row);
    const b = row.querySelector('[data-testid=parameter-edit-btn]');
    return b && vis(b) ? center(b) : null;
  };
  const paramRows = () => {
    const menu = [...document.querySelectorAll('[data-testid=parameter-submenu]')].find(vis);
    if (!menu) return null;
    return [...menu.querySelectorAll('.ui-menu__section')].map(s => ({
      title: s.querySelector('.ui-menu__section-title')?.innerText.trim() || '',
      rows: [...s.querySelectorAll('.ui-menu__row, .ui-menu__toggle-row')].filter(vis).map(r => {
        const toggle = r.matches('.ui-menu__toggle-row');
        return { el: r, label: (r.querySelector('.ui-menu__item-content') || r).innerText.trim(), toggle, checked: toggle ? r.getAttribute('aria-checked') === 'true' : !!r.querySelector('.ui-model-picker__param-check') };
      }),
    }));
  };
  const modelParams = () => paramRows()?.map(s => ({ title: s.title, items: s.rows.map(({ el, ...rest }) => rest) })) || null;
  const paramTarget = (title, label) => {
    const row = paramRows()?.find(s => s.title === title)?.rows.find(r => r.label === label);
    return row ? center(row.el) : null;
  };

  const menuItem = (kind, label) => {
    const hit = (MENU_ITEMS[kind]?.() || []).find(x => vis(x.el) && x.label === label);
    if (!hit) return null;
    hit.el.scrollIntoView({ block: 'nearest' });
    return center(hit.el);
  };

  // Returns the button's on-screen center so the click goes through real mouse events.
  const locate = (item, n) => {
    const panel = findPanel();
    if (!panel) return null;
    const root = item === 'g' ? panel
      : panel.querySelector(`[data-flat-index="${CSS.escape(item)}"]`)
        || panel.querySelector(`[data-message-id="${CSS.escape(item)}"]`)
        || [...panel.querySelectorAll(ITEM)].find(e => itemId(e) === item);
    if (!root) return null;
    const list = item === 'g' ? buttonsIn(panel, true).filter(x => !inItem(x.el) && !inQn(x.el)) : buttonsIn(root, false);
    const b = list[n]?.el;
    if (!b) return null;
    b.scrollIntoView({ block: 'center' });
    const r = b.getBoundingClientRect();
    return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
  };

  const panelRect = () => {
    const p = findPanel();
    if (!p) return null;
    const r = p.getBoundingClientRect();
    return [r.x, r.y, r.width, r.height].map(Math.round);
  };

  // Cheap per-window check for notifications: is the Agent running, and is it waiting on a button?
  // "Run in background" shows up on any command running longer than ~5 s; it is not a request for approval.
  const WAIT = /^(?!.*(background|后台))(run|allow|accept|approve|continue|yes\b|运行|允许|接受|批准|继续)/i;
  const agentState = () => {
    const panel = findPanel();
    if (!panel) return null;
    const busy = isBusy();
    const items = topItems(panel);
    let waiting = null, waitingText = '';
    for (const el of items.slice(-3)) {
      const labels = buttonsIn(el, false).map(x => x.label).filter(l => WAIT.test(l));
      if (labels.length) { waiting = `${itemId(el)}:${labels.join('/')}`; waitingText = summary(el); }
    }
    const global = buttonsIn(panel, true).filter(x => !inItem(x.el) && !inQn(x.el) && WAIT.test(x.label)).map(x => x.label);
    if (!waiting && global.length) { waiting = `g:${global.join('/')}`; waitingText = global.join(' / '); }
    const qs = questions();
    if (qs?.list.length) {
      const text = qs.list.map(q => q.text).join(' / ');
      waiting = `q:${text.slice(0, 120)}`;
      waitingText = text;
    }
    const lastReply = [...items].reverse().find(el => el.matches('[data-message-kind="assistant"]') || el.querySelector('[data-message-kind="assistant"]'));
    const last = lastReply ? lastReply.innerText.trim().replace(/\s+/g, ' ').slice(0, 160) : '';
    return { busy, waiting, waitingText, last };
  };

  const quickOpenRows = () => {
    const w = document.querySelector('.quick-input-widget');
    if (!w || !vis(w)) return null;
    return [...w.querySelectorAll('.monaco-list-row')].filter(vis).map(e => ({
      name: e.querySelector('.label-name')?.innerText.trim() || '',
      dir: e.querySelector('.label-description')?.innerText.trim() || '',
    }));
  };

  return { extract, locate, panelRect, control, menuItems, menuItem, quickOpenRows, agentState, queueTarget, stopTarget, focusQueueEdit,
    menuOpen, maxToggle, modelTarget, modelParams, paramTarget, subTarget, activeChatTab, tabTarget, hasComposer, filesState, fileTarget, questionTarget };
}

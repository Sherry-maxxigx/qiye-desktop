(function () {
  'use strict';
  const token = __HOMEWORK_TOKEN__, testing = __HOMEWORK_TEST__;
  const trusted = origin => { try { const u = new URL(origin); return u.protocol === 'https:' &&
    (u.hostname === 'chaoxing.com' || u.hostname.endsWith('.chaoxing.com') || testing && u.hostname === 'player-test.invalid'); } catch (_) { return false; } };
  if (!trusted(location.origin) || window.__qiyeHomework) return;
  window.__qiyeHomework = true;
  const frameId = Math.random().toString(36).slice(2), targets = new Map(), works = new Map();
  let listSignature = '', lastState = '', menuAttempts = 0, lastMenuAttempt = 0, menuOpened = false;
  const startedAt = Date.now();
  const text = node => (node && (node.innerText || node.textContent) || '').replace(/\s+/g, ' ').trim();
  const visible = node => !!node && node.getClientRects().length > 0 && getComputedStyle(node).visibility !== 'hidden' && getComputedStyle(node).display !== 'none';
  function emit(data) {
    const message = Object.assign({ channel: 'qiye-homework', token, frameId }, data);
    if (window === top) window.chrome.webview.postMessage(message); else top.postMessage(message, '*');
  }
  function state(code) { if (lastState !== code) { lastState = code; emit({ kind: 'state', code }); } }
  function pending(node) {
    const status = text(node.querySelector('.work-status,.workStatus,.status,.state,.statusTxt,.notFinish')) || text(node);
    return /未交|未提交|待完成|未完成|待做|待作答|继续作答|待提交|进行中/.test(status) &&
      !/已提交|已完成|已交|待批阅|已批阅|已截止|已过期|已结束|未开始|未开放|未发布/.test(status + ' ' + text(node.querySelector('.time')));
  }
  function workUrl(action) {
    const values = [action.getAttribute('href'), action.getAttribute('data-url')];
    // The official goTask handler opens the card's data attribute, not href.
    if (/^\s*goTask\s*\(/.test(action.getAttribute('onclick') || '')) values.push(action.getAttribute('data'));
    for (const value of values) {
      if (!value) continue;
      try {
        const url = new URL(value, location.href);
        if (url.protocol === 'http:') url.protocol = 'https:';
        if (trusted(url.origin) && /\/work\//i.test(url.pathname)) return url.href;
      } catch (_) { }
    }
    return '';
  }
  function workList(force) {
    const rows = [];
    const nodes = Array.from(document.querySelectorAll('.bottomList li,.workList li,.work-list li,.workUl li,.work_item,.work-item,.workItem,.work-list-item,li[data-workid],li[data-work-id]'));
    for (const link of document.querySelectorAll('a[href*="/work/"],a[onclick*="work"],a[onclick*="Work"]')) {
      const card = link.closest('li,.work-item,.workItem,.work_item,.work-list-item');
      if (card) nodes.push(card);
    }
    const seen = new Set(); works.clear();
    for (const node of nodes) {
      if (seen.has(node) || !visible(node) || !pending(node)) continue;
      seen.add(node);
      const actions = Array.from(node.querySelectorAll('a,button,[onclick]')).filter(visible);
      const action = node.matches('.bottomList li[onclick]') && /^\s*goTask\s*\(/.test(node.getAttribute('onclick') || '') ? node :
        actions.find(a => /^(继续作答|开始作答|去完成|去作答|答题|做作业)$/.test(text(a))) ||
        actions.find(a => /\/work\/|work|Work/.test((a.getAttribute('href') || '') + ' ' + (a.getAttribute('onclick') || '')));
      if (!action) continue;
      const titleNode = node.querySelector('.workName,.work-name,.right-content .overHidden2,.title,.tit,h3,h4,[title]');
      const title = (titleNode && (titleNode.getAttribute('title') || text(titleNode))) || text(action) || text(node).slice(0, 100);
      const key = String(rows.length);
      const safeTitle = title.slice(0, 160);
      works.set(key, { node, action, title: safeTitle });
      const url = workUrl(action);
      let id = node.getAttribute('data-workid') || node.getAttribute('data-work-id') || '';
      if (!id && url) { const params = new URL(url).searchParams; id = params.get('workId') || params.get('workid') || ''; }
      if (!/^\d{1,24}$/.test(id)) id = '';
      rows.push({ key, id, title: safeTitle, url, detail: text(node).slice(0, 220) });
    }
    const known = nodes.length > 0 || !!document.querySelector('.bottomList,.workList,.work-list,.workUl,.work-list-box') || /暂无作业|没有作业|暂无未完成作业/.test(text(document.body));
    const signature = JSON.stringify({ known, rows });
    if (force || signature !== listSignature) { listSignature = signature; emit({ kind: 'list', known, rows }); }
  }
  function enterMenu() {
    if (window !== top || menuOpened || !/\/mycourse\/(stu|studentcourse)\b/i.test(location.pathname)) return;
    // The signed list URL is produced by the official menu handler. Never
    // navigate to its unsigned data-url or guess the work validation values.
    const existing = Array.from(document.querySelectorAll('iframe')).some(frame => {
      try { return /-zy$/.test(frame.id) || /\/work\/(list|getAllWork)\b/i.test(new URL(frame.src, location.href).pathname); } catch (_) { return false; }
    });
    if (existing) { menuOpened = true; state('loading-list'); return; }
    const anchor = document.querySelector('a[title="作业"],a[title="课程作业"]');
    const menu = document.querySelector('.nav-content li[dataname="zy"],li[dataname="zy"]') ||
      (anchor && (anchor.closest('li') || anchor)) ||
      Array.from(document.querySelectorAll('a,li[onclick],[role="tab"]')).find(e => visible(e) && /^(作业|课程作业)(?:\s*\(?\d+\)?)?$/.test(text(e)));
    if (!menu || !visible(menu)) {
      if (Date.now() - startedAt > 12000) state(/\/studentcourse\b/i.test(location.pathname) ? 'chapter-page' : 'missing-menu');
      return;
    }
    if (menuAttempts >= 3) { if (Date.now() - lastMenuAttempt >= 5000) state('menu-timeout'); return; }
    if (document.readyState === 'loading' || Date.now() - lastMenuAttempt < 3000) return;
    lastMenuAttempt = Date.now(); menuAttempts++; state('loading-list'); menu.click();
  }
  function checkReadState() {
    const body = text(document.body).slice(0, 2000);
    if (/passport2?\.chaoxing\.com$/i.test(location.hostname) || /\/login\b/i.test(location.pathname) && document.querySelector('input[type="password"]')) state('login-required');
    else if (/无权访问|无效参数|访问被拒绝|没有访问权限|您没有权限/.test(body)) state('access-denied');
    else if (/\/work\/(list|getAllWork)\b/i.test(location.pathname) && Date.now() - startedAt > 20000 &&
      !document.querySelector('.bottomList,.workList,.work-list,.workUl,.work-list-box') && !/暂无作业|没有作业|暂无未完成作业/.test(body)) state('list-timeout');
  }
  function hash(value) { let h = 2166136261; for (let i = 0; i < value.length; i++) h = Math.imul(h ^ value.charCodeAt(i), 16777619); return String(h >>> 0); }
  function questionNodes() {
    if (!testing && !/\/work\//i.test(location.pathname)) return [];
    let rows = Array.from(document.querySelectorAll('.singleQuesId[data]'));
    if (!rows.length) rows = Array.from(document.querySelectorAll('.questionLi'));
    return rows.filter(visible).slice(0, 101);
  }
  function editorFor(field) {
    if (!window.UE || !UE.instants || !field.id) return null;
    return Object.values(UE.instants).find(editor => editor && (editor.key === field.id || editor.textarea === field)) || null;
  }
  function fieldValue(field) {
    const editor = editorFor(field);
    if (!editor) return field.isContentEditable ? text(field) : (field.value || '').trim();
    if (!editor.isReady) return (field.value || '').trim();
    const value = String(editor.getContentTxt()).trim();
    return value || (editor.hasContents() ? '[已有非文本内容]' : '');
  }
  function writable(field) {
    const editor = editorFor(field);
    return !field.disabled && !field.readOnly && (!editor || editor.isReady && editor.body && editor.body.isContentEditable &&
      (typeof editor.queryCommandState !== 'function' || editor.queryCommandState('source') !== 1));
  }
  function writeField(field, value) {
    const editor = editorFor(field);
    if (editor) {
      const escaped = document.createElement('div'); escaped.textContent = value;
      editor.setContent(escaped.innerHTML.replace(/\r?\n/g, '<br>'));
      editor.fireEvent('contentChange'); editor.sync();
    } else {
      if (field.isContentEditable) field.textContent = value; else field.value = value;
      field.dispatchEvent(new Event('input', { bubbles: true })); field.dispatchEvent(new Event('change', { bubbles: true }));
    }
  }
  function questionTitle(stem) {
    if (!stem) return '';
    const copy = stem.cloneNode(true);
    copy.querySelectorAll('.colorShallow,.question-type,.type_tit,script,style').forEach(e => e.replaceWith(document.createTextNode(' ')));
    let title = text(copy);
    for (let i = 0; i < 3; i++) title = title.replace(/^\d+\s*[.、．](?!\d)\s*/, '')
      .replace(/^[【\[(（](?:单选题|多选题|判断题|填空题|简答题)[】\])）]\s*/, '').trim();
    return title;
  }
  function parseQuestion(node, index) {
    const stem = node.querySelector('.Zy_TItle,.Cy_TItle,.mark_name,.question-title,.questionTitle');
    const title = questionTitle(stem);
    const timu = node.querySelector('.TiMu[data]');
    const typeText = (node.getAttribute('typeName') || '') + ' ' + text(stem) + ' ' + text(node.querySelector('.question-type,.type_tit'));
    let type = timu ? ({ '0': 'single', '1': 'multiple', '2': 'fill', '3': 'judgement', '4': 'short' }[timu.getAttribute('data')] || 'unsupported') :
      /判断/.test(typeText) ? 'judgement' : /多选/.test(typeText) ? 'multiple' : /单选/.test(typeText) ? 'single' : /填空/.test(typeText) ? 'fill' : /简答/.test(typeText) ? 'short' : 'unsupported';
    const id = node.getAttribute('data') || (node.getAttribute('data-id') || '').replace(/^question/, '') || String(index + 1);
    const answer = document.getElementById('answer' + id);
    const input = answer && node.contains(answer) ? answer : null;
    const options = [];
    // Current dowork pages use div.answerBg, while older pages use li.
    for (const li of node.querySelectorAll('.answerBg,li')) {
      const marker = li.querySelector('.num_option[data],.num_option_dx[data]');
      const native = li.querySelector('input[type="radio"],input[type="checkbox"]');
      if (!marker && !native) continue;
      const raw = text(li), prefix = /^([A-Z])[\s、.．:：)）]+([\s\S]+)$/.exec(raw);
      const markerLetter = marker && /^([A-Z])$/.exec(marker.getAttribute('data'));
      const letter = markerLetter ? markerLetter[1] : prefix ? prefix[1] : String.fromCharCode(65 + options.length);
      const value = marker ? marker.getAttribute('data') : letter;
      const copy = li.cloneNode(true); copy.querySelectorAll('.num_option,.num_option_dx,input').forEach(e => e.remove());
      const contentNode = li.querySelector('.answer_p');
      const content = contentNode ? text(contentNode) : marker ? text(copy) : prefix ? prefix[2].trim() : text(copy);
      options.push({ letter, value, content, marker, native, li });
    }
    const fields = Array.from(node.querySelectorAll('textarea,input[type="text"],[contenteditable="true"]')).filter(field => {
      const editor = editorFor(field);
      return editor ? editor.isReady && visible(editor.container) : visible(field);
    });
    const answered = !!(input && input.value.trim()) || options.some(o => o.native ? o.native.checked : /check_answer|check_answer_dx/.test(o.marker.className)) || fields.some(f => fieldValue(f));
    if (!/^\d{1,24}$/.test(id) || !title || title.length > 4000 || options.some(o => !o.content || o.content.length > 4000) ||
        new Set(options.map(o => o.letter)).size !== options.length) type = 'unsupported';
    if (/^(single|multiple|judgement)$/.test(type) && (options.length < 2 || options.length > 26)) type = 'unsupported';
    if (/^(fill|short)$/.test(type) && (fields.length < 1 || fields.length > 20)) type = 'unsupported';
    return { node, input, options, fields, row: { id, title, type, answered, blankCount: fields.length, options: options.map(o => o.letter + ' ' + o.content) } };
  }
  function page() {
    const questions = questionNodes().map(parseQuestion);
    if (!questions.length || questions.length > 100) return null;
    if (new Set(questions.map(q => q.row.id)).size !== questions.length) return null;
    const fingerprint = hash(JSON.stringify(questions.map(q => [q.row.id, q.row.title, q.row.type, q.row.options, q.row.blankCount])));
    return { questions, fingerprint };
  }
  const canonical = value => /^(true|false)$/i.test(String(value || '')) ? String(value).toLowerCase() : String(value || '').split('').sort().join('');
  function fill(command) {
    const current = page();
    if (!current || current.fingerprint !== command.fingerprint || document.hidden) return { ok: false, reason: 'page-changed' };
    const q = current.questions.find(q => q.row.id === command.answer.id);
    if (!q || q.row.type !== command.answer.type) return { ok: false, reason: 'question-changed' };
    if (q.row.answered) return { ok: true, skipped: true, reason: 'already-answered' };
    if (q.row.type === 'fill' || q.row.type === 'short') {
      const values = command.answer.values;
      if (!Array.isArray(values) || values.length !== q.fields.length || values.some(v => typeof v !== 'string' || !v.trim() || v.length > 12000) ||
          q.fields.some(f => !writable(f))) return { ok: false, reason: 'unsupported-fields' };
      q.fields.forEach((field, i) => writeField(field, values[i]));
      if (q.fields.some((f, i) => fieldValue(f) !== values[i].trim())) return { ok: false, reason: 'answer-not-applied' };
      return { ok: true };
    }
    if (!/^(single|multiple|judgement)$/.test(q.row.type)) return { ok: false, reason: 'unsupported-type' };
    const selection = canonical(command.answer.selection);
    let chosen;
    if (q.row.type === 'judgement') {
      if (!/^(true|false)$/.test(selection)) return { ok: false, reason: 'invalid-answer' };
      chosen = q.options.filter(o => (selection === 'true' ? /^(对|正确|是|true|√)$/i : /^(错|错误|否|false|×)$/i).test(o.content));
      if (chosen.length !== 1) return { ok: false, reason: 'option-changed' };
    } else {
      if (!/^[A-Z]+$/.test(selection) || q.row.type === 'single' && selection.length !== 1) return { ok: false, reason: 'invalid-answer' };
      chosen = q.options.filter(o => selection.includes(o.letter));
      if (chosen.length !== selection.length) return { ok: false, reason: 'option-changed' };
    }
    if (q.options.some(o => o.native && o.native.disabled)) return { ok: false, reason: 'disabled-options' };
    for (const option of chosen) { if (option.native) option.native.click(); else option.li.click(); }
    if (q.input ? canonical(q.input.value) !== selection : q.options.some(o => !o.native || o.native.checked !== chosen.includes(o))) return { ok: false, reason: 'answer-not-applied' };
    return { ok: true };
  }
  function receive(command) {
    if (command.channel !== 'qiye-homework-command' || command.token !== token) return;
    if (command.frameId && command.frameId !== frameId) {
      const target = targets.get(command.frameId); if (window === top && target) target.postMessage(command, '*'); return;
    }
    let result;
    try {
      if (command.action === 'read-list') { enterMenu(); workList(true); return; }
      if (command.action === 'open-work') {
        const work = works.get(command.key);
        if (work && work.title === command.title && work.node.isConnected && visible(work.node) && pending(work.node)) work.action.click();
        return;
      }
      if (command.action === 'snapshot') {
        const current = page(); result = current ? { ok: true, fingerprint: current.fingerprint, questions: current.questions.map(q => q.row) } : { ok: false, reason: 'not-homework' };
      } else if (command.action === 'fill') result = fill(command);
      else return;
    } catch (_) { result = { ok: false, reason: 'page-operation-failed' }; }
    emit(Object.assign({ kind: 'result', request: command.request }, result));
  }
  window.addEventListener('message', event => {
    if (!trusted(event.origin) || !event.data || event.data.token !== token) return;
    if (event.data.channel === 'qiye-homework' && window === top) {
      targets.set(event.data.frameId, event.source); window.chrome.webview.postMessage(event.data);
    } else if (event.data.channel === 'qiye-homework-command' && event.source === top) receive(event.data);
  });
  if (window === top) window.chrome.webview.addEventListener('message', event => receive(event.data || {}));
  function sample() {
    enterMenu(); workList(false); checkReadState();
    const current = page();
    if (current) emit({ kind: 'page', fingerprint: current.fingerprint, total: current.questions.length, pageVisible: !document.hidden });
    else emit({ kind: 'page', fingerprint: '', total: 0, pageVisible: !document.hidden });
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', sample, { once: true }); else sample();
  setInterval(sample, 1000);
})();

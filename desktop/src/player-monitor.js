(function installStudyPlayerMonitor() {
  'use strict';
  const token = __PLAYER_TOKEN__;
  const test = __PLAYER_TEST__;
  const trusted = host => host === 'chaoxing.com' || host.endsWith('.chaoxing.com') ||
    (test && host === 'player-test.invalid');
  if (!/^https?:$/.test(location.protocol) || !trusted(location.hostname) || window.__studyPlayerMonitor) return;
  window.__studyPlayerMonitor = true;
  const frameId = Math.random().toString(36).slice(2);
  const frameTargets = new Map();
  const monitorRepairs = new WeakMap();
  let selectedVideo = null;
  const states = new WeakMap();
  let mediaSequence = 0;
  let preferredMediaId = '';
  let filledQuiz = null;
  let quizSubmission = 0;
  const videoQuizIds = new WeakMap();
  let videoQuizSequence = 0;
  let sampledVideoQuiz = false;
  let lastTaskPlan = '', lastTaskPlanAt = 0;
  // New documents wait for the desktop's current session state, including child frames.
  let studyPaused = true;
  let studyVersion = -1;
  function pauseMedia() {
    document.querySelectorAll('video,audio').forEach(media => {
      stateOf(media).manualPause = true;
      if (!media.paused) media.pause();
    });
  }
  function holdPlayback(event) {
    if (studyPaused && event.target instanceof HTMLMediaElement) {
      stateOf(event.target).manualPause = true;
      event.target.pause();
      requestManualPlayback(event.target);
    }
  }
  document.addEventListener('play', holdPlayback, true);
  document.addEventListener('playing', holdPlayback, true);
  function activeVideoQuiz() {
    if (!test && !/\/ananas\/modules\/video\/index\.html$/i.test(location.pathname)) return null;
    const roots = Array.from(document.querySelectorAll('.ans-videoquiz')).filter(visible);
    return roots.length === 1 ? roots[0] : null;
  }
  function videoQuizPage() {
    const root = activeVideoQuiz();
    if (!root) return null;
    const titleNode = root.querySelector('.tkItem_title');
    const title = (titleNode && titleNode.innerText || '').trim();
    const typeText = (root.querySelector('.tkTopic_title') || {}).innerText || '';
    const rows = Array.from(root.querySelectorAll('.ans-videoquiz-opt'));
    const options = rows.map((row, index) => {
      const input = row.querySelector('input[type="radio"],input[type="checkbox"]');
      const text = (row.innerText || '').replace(/\s+/g, ' ').trim();
      const match = /^([A-Z])[\s、.．:：)）]+([\s\S]+)$/.exec(text);
      return { input, letter: match ? match[1] : String.fromCharCode(65 + index), content: match ? match[2].trim() : text };
    });
    let type = /判断|true\s*(?:or|\/)\s*false/i.test(typeText) ? 'judgement'
      : options.length && options.every(x => x.input && x.input.type === 'checkbox') ? 'multiple'
      : /单选|single/i.test(typeText) && options.every(x => x.input && x.input.type === 'radio') ? 'single' : 'unsupported';
    if (!title || title.length > 4000 || options.length < 2 || options.length > 26 || options.some(x => !x.input || !x.content || x.content.length > 4000)
        || new Set(options.map(x => x.letter)).size !== options.length) type = 'unsupported';
    // Only visible question/option text is read. Input values and player configuration
    // can contain answer keys or member tokens and must never be used for lookup.
    const source = type + ':' + title + ':' + options.map(x => x.letter + ':' + x.content).join('|');
    let hash = 2166136261;
    for (let i = 0; i < source.length; i++) hash = Math.imul(hash ^ source.charCodeAt(i), 16777619);
    if (!videoQuizIds.has(root)) videoQuizIds.set(root, ++videoQuizSequence);
    const id = String(hash >>> 0);
    return { root, options, question: { id, title, type, options: options.map(x => x.letter + ' ' + x.content) },
      fingerprint: 'video:' + videoQuizIds.get(root) + ':' + id };
  }
  function videoFeedback(root, selector) {
    return Array.from(root.querySelectorAll(selector)).some(e => {
      // The official callback may remove the popup immediately. A detached node has
      // no computed style; retain only an explicit official display change then.
      const style = getComputedStyle(e);
      const display = style.display || e.style.display;
      return display && display !== 'none' && style.visibility !== 'hidden' && e.style.visibility !== 'hidden'
        && (e.innerText || e.textContent || '').trim().length > 0;
    });
  }
  function videoQuizPlan(page, answers) {
    if (page.question.type === 'unsupported') throw new Error('video-question-unsupported');
    if (!Array.isArray(answers) || answers.length !== 1 || answers[0].id !== page.question.id || answers[0].type !== page.question.type)
      throw new Error('question-changed');
    const selection = quizSelection(answers[0].selection);
    let letters;
    if (page.question.type === 'judgement') {
      if (!/^(true|false)$/.test(selection)) throw new Error('invalid-answer');
      const match = page.options.filter(x => (selection === 'true' ? /^(对|正确|是|true|√)$/i : /^(错|错误|否|false|×)$/i).test(x.content));
      if (match.length !== 1) throw new Error('option-changed');
      letters = [match[0].letter];
    } else {
      if (!/^[A-Z]+$/.test(selection) || page.question.type === 'single' && selection.length !== 1) throw new Error('invalid-answer');
      letters = selection.split('');
    }
    if (letters.some(letter => page.options.filter(x => x.letter === letter).length !== 1) || page.options.some(x => x.input.disabled))
      throw new Error('option-changed-or-disabled');
    return letters;
  }
  function canContinueRejected(page) {
    const button = page.root.querySelector('#videoquiz-continue.ans-videoquiz-continue');
    return videoFeedback(page.root, '#spanNot,#spanNotBack,#spanNotBackPoint') && button && visible(button)
      && button.getAttribute('aria-disabled') !== 'true' && !button.classList.contains('disabled');
  }
  async function videoQuizCommand(command, page) {
    let submitted = false;
    try {
      if (command.action === 'quiz-snapshot') {
        if (videoFeedback(page.root, '#spanNot,#spanNotBack,#spanNotBackPoint') && !(command.allowRejectedContinue && canContinueRejected(page))) throw new Error('video-answer-rejected');
        quizReply(command, true, '', { mode: 'video', total: 1, questions: [page.question],
          accepted: videoFeedback(page.root, '#spanHas'), canContinueRejected: Boolean(canContinueRejected(page)) }); return;
      }
      if (command.action === 'quiz-fill') {
        const letters = videoQuizPlan(page, command.answers);
        for (const option of page.options) {
          const expected = letters.includes(option.letter);
          if (option.input.checked !== expected && (expected || option.input.type === 'checkbox')) option.input.click();
        }
        if (page.options.some(x => x.input.checked !== letters.includes(x.letter))) throw new Error('answer-not-applied');
        filledQuiz = { fingerprint: page.fingerprint, answers: command.answers };
        quizReply(command, true); return;
      }
      if (command.action !== 'quiz-submit') return;
      const generation = ++quizSubmission;
      if (!videoFeedback(page.root, '#spanHas') && !(command.allowRejectedContinue && canContinueRejected(page))) {
        if (!filledQuiz || filledQuiz.fingerprint !== page.fingerprint) throw new Error('quiz-not-filled');
        const letters = videoQuizPlan(page, filledQuiz.answers);
        if (page.options.some(x => x.input.checked !== letters.includes(x.letter))) throw new Error('answer-changed-before-submit');
        const submit = page.root.querySelector('#videoquiz-submit.ans-videoquiz-submit');
        if (!submit || !visible(submit)) throw new Error('submit-button-unavailable');
        submit.click(); submitted = true;
      }
      let continued = false;
      const deadline = Date.now() + 12000;
      while (Date.now() < deadline) {
        await new Promise(resolve => setTimeout(resolve, 250));
        if (generation !== quizSubmission || document.hidden) { quizReply(command, false, 'cancelled', { submitted }); return; }
        if (confirmationPrompt()) { quizReply(command, false, 'verification-required', { submitted }); return; }
        const current = videoQuizPage();
        if (current && current.fingerprint !== page.fingerprint) { quizReply(command, false, 'question-changed', { submitted }); return; }
        const submitting = page.root.querySelector('#videoquiz-submitting');
        if (submitting && visible(submitting)) continue;
        // Some official video quizzes leave the previous wrong-answer label on screen
        // after a later correct response. A new correct response takes precedence.
        if (videoFeedback(page.root, '#spanHas')) {
          const next = page.root.querySelector('#videoquiz-continue.ans-videoquiz-continue');
          if (current && !continued && next && visible(next)) { next.click(); continued = true; }
          if (!activeVideoQuiz()) { quizReply(command, true, '', { submitted, completed: true }); return; }
          continue;
        }
        if (videoFeedback(page.root, '#spanNot,#spanNotBack,#spanNotBackPoint')) {
          if (command.allowRejectedContinue && canContinueRejected(page)) {
            if (!continued) { page.root.querySelector('#videoquiz-continue.ans-videoquiz-continue').click(); continued = true; }
            if (!activeVideoQuiz()) { quizReply(command, true, '', { submitted, completed: false, continued: true }); return; }
            continue;
          }
          if (command.allowRejectedContinue && continued && !activeVideoQuiz()) {
            quizReply(command, true, '', { submitted, completed: false, continued: true }); return;
          }
          const submit = page.root.querySelector('#videoquiz-submit.ans-videoquiz-submit');
          const retryAllowed = Boolean(submitting && current && submit && visible(submit)
            && !videoFeedback(page.root, '#spanNotBack,#spanNotBackPoint')
            && submit.getAttribute('aria-disabled') !== 'true' && !submit.classList.contains('disabled'));
          quizReply(command, false, 'video-answer-rejected', { submitted, retryAllowed }); return;
        }
        if (!current) {
          quizReply(command, false, 'video-result-unconfirmed', { submitted }); return;
        }
      }
      quizReply(command, false, 'video-result-unconfirmed', { submitted });
    } catch (error) { quizReply(command, false, error.message || 'video-quiz-operation-failed', { submitted }); }
  }
  function quizPage() {
    if (!test && !/\/work\/doHomeWorkNew\b/i.test(location.pathname)) return null;
    const form = document.querySelector('form#form1');
    if (!form) return null;
    const questions = Array.from(form.querySelectorAll('.singleQuesId[data]'));
    if (!questions.length || questions.length > 100) return null;
    const source = questions.map(q => q.getAttribute('data') + ':' + (q.textContent || '').trim()).join('|');
    let hash = 2166136261;
    for (let i = 0; i < source.length; i++) hash = Math.imul(hash ^ source.charCodeAt(i), 16777619);
    return { form, questions, fingerprint: String(hash >>> 0) + ':' + questions.length };
  }
  const quizSelection = value => String(value || '').toLowerCase() === 'true' || String(value || '').toLowerCase() === 'false'
    ? String(value).toLowerCase() : String(value || '').split('').sort().join('');
  function quizReply(command, ok, reason, extra) {
    emit(Object.assign({ kind: 'quiz-result', request: command.request, ok, reason: reason || '' }, extra || {}));
  }
  function quizPlan(page, answers) {
    if (!Array.isArray(answers) || answers.length !== page.questions.length) throw new Error('question-count-changed');
    const ids = new Set();
    return answers.map(answer => {
      if (!/^\d+$/.test(answer.id) || ids.has(answer.id)) throw new Error('invalid-question-id');
      ids.add(answer.id);
      const q = page.questions.find(e => e.getAttribute('data') === answer.id);
      const type = { single: '0', multiple: '1', judgement: '3' }[answer.type];
      const input = document.getElementById('answer' + answer.id);
      if (!q || !type || !q.querySelector('.TiMu[data="' + type + '"]') || !input || !q.contains(input)) throw new Error('question-type-changed');
      const selection = quizSelection(answer.selection);
      if (answer.type === 'judgement' ? !/^(true|false)$/.test(selection) : !/^[A-Z]+$/.test(selection)) throw new Error('invalid-answer');
      if (answer.type === 'single' && selection.length !== 1) throw new Error('invalid-answer');
      const values = answer.type === 'judgement' ? [selection] : selection.split('');
      const options = Array.from(q.querySelectorAll('ul li')).map(li => ({ li, marker: li.querySelector('.num_option[data],.num_option_dx[data]') })).filter(x => x.marker);
      for (const value of values) if (options.filter(x => x.marker.getAttribute('data') === value).length !== 1) throw new Error('option-changed');
      return { input, selection, values, options, type: answer.type };
    });
  }
  function quizConfirmation() {
    const documents = [document];
    try { if (top.document !== document) documents.push(top.document); } catch (_) {}
    for (const doc of documents) {
      const legacy = doc.getElementById('confirmSubWin');
      if (legacy && visible(legacy)) {
        if (/未做完|未作答|尚有.*未完成/.test(legacy.innerText || '')) return { reason: 'unanswered-question' };
        const button = legacy.querySelector('a.bluebtn[onclick^="submitCheckTimes"]');
        return button && visible(button) ? { button } : { reason: 'confirmation-unrecognized' };
      }
      const dialog = doc.getElementById('workpop');
      if (!dialog || !visible(dialog)) continue;
      const content = dialog.querySelector('#popcontent');
      const text = (content && content.innerText || '').replace(/\s+/g, '').trim();
      if (/验证码|身份验证|人脸|请.*验证/.test(text)) return { reason: 'verification-required' };
      if (/未做完|未作答|未完成/.test(text)) return { reason: 'unanswered-question' };
      const button = dialog.querySelector('#popok');
      if (!/^(确认|确定)提交(?:本次(?:测验|作业))?[?？。！!]*$/.test(text) || !button || !visible(button)
          || !/^(提交|确定|确认)$/.test((button.innerText || '').trim())) return { reason: 'confirmation-unrecognized' };
      return { button };
    }
    return null;
  }
  async function quizCommand(command) {
    if (command.action === 'quiz-cancel') { quizSubmission++; return; }
    const videoPage = videoQuizPage();
    const page = videoPage || quizPage();
    if (!page || page.fingerprint !== command.fingerprint || document.hidden || confirmationPrompt()) {
      quizReply(command, false, 'page-changed-or-confirmation'); return;
    }
    if (videoPage) { await videoQuizCommand(command, videoPage); return; }
    try {
      if (command.action === 'quiz-snapshot') {
        quizReply(command, true, '', { html: document.documentElement.outerHTML, total: page.questions.length }); return;
      }
      if (command.action === 'quiz-fill') {
        const plan = quizPlan(page, command.answers);
        for (const item of plan) {
          if (item.type === 'multiple') {
            for (const option of item.options) {
              const expected = item.values.includes(option.marker.getAttribute('data'));
              const checked = option.marker.classList.contains('check_answer_dx');
              if (checked !== expected) option.li.click();
            }
          } else if (quizSelection(item.input.value) !== item.selection) {
            item.options.find(x => x.marker.getAttribute('data') === item.values[0]).li.click();
          }
          if (quizSelection(item.input.value) !== item.selection) throw new Error('answer-not-applied');
        }
        filledQuiz = { fingerprint: page.fingerprint, answers: command.answers };
        quizReply(command, true); return;
      }
      if (command.action !== 'quiz-submit') return;
      if (!filledQuiz || filledQuiz.fingerprint !== page.fingerprint) throw new Error('quiz-not-filled');
      const plan = quizPlan(page, filledQuiz.answers);
      if (plan.some(x => quizSelection(x.input.value) !== x.selection)) throw new Error('answer-changed-before-submit');
      const button = document.querySelector('.btnSubmit.workBtnIndex,.Btn_blue_1');
      if (!button || !visible(button)) throw new Error('submit-button-unavailable');
      const generation = ++quizSubmission;
      button.click();
      const deadline = Date.now() + 12000;
      while (Date.now() < deadline) {
        await new Promise(resolve => setTimeout(resolve, 500));
        if (generation !== quizSubmission) { quizReply(command, false, 'cancelled'); return; }
        if (confirmationPrompt()) { quizReply(command, false, 'verification-required', { submitted: true }); return; }
        const current = quizPage();
        if (!current || current.fingerprint !== page.fingerprint) { quizReply(command, true, '', { submitted: true }); return; }
        const confirmation = quizConfirmation();
        if (confirmation) {
          if (confirmation.reason) { quizReply(command, false, confirmation.reason, { submitted: false }); return; }
          confirmation.button.click();
          quizReply(command, true, '', { submitted: true }); return;
        }
      }
      quizReply(command, false, 'submission-not-confirmed', { submitted: false });
    } catch (error) { quizReply(command, false, error.message || 'quiz-operation-failed', { submitted: false }); }
  }
  function navigation() {
    if (window !== top) return null;
    const frame = document.querySelector('iframe#iframe');
    const labels = Array.from(document.querySelectorAll('.spanText'));
    const tabs = labels.map(label => label.closest('li')).filter(Boolean);
    if (!frame || !tabs.length || tabs.length !== labels.length) return null;
    let url;
    try { url = new URL(frame.src, location.href); } catch (_) { return null; }
    const knowledgeId = url.searchParams.get('knowledgeid') || url.searchParams.get('chapterId') || '';
    const card = Number(url.searchParams.get('num'));
    if (!/^\d+$/.test(knowledgeId) || !url.searchParams.has('num') || !Number.isInteger(card) || card < 0 || card >= tabs.length) return null;
    const chapters = Array.from(document.querySelectorAll('.posCatalog_select[id^="cur"]')).filter(e => /^cur\d+$/.test(e.id));
    const index = chapters.findIndex(e => e.id === 'cur' + knowledgeId);
    const next = index >= 0 ? chapters[index + 1] : null;
    const locked = next && (next.getAttribute('aria-disabled') === 'true' || /locked|disabled/.test(next.className) || next.querySelector('.lock,.posCatalog_lock,.icon-lock'));
    const title = next && next.querySelector('.posCatalog_name');
    // Locked entries can be present before the platform attaches its official click handler.
    const ready = next && (typeof next.onclick === 'function' ||
      (title && (typeof title.onclick === 'function' || title.closest('a[href]'))) || next.matches('a[href]'));
    return { knowledgeId, card, tabs, next,
      nextKnowledgeId: next ? next.id.slice(3) : '', nextLocked: Boolean(locked), nextReady: Boolean(ready), catalogueKnown: index >= 0 };
  }
  function navigate(command) {
    if (window !== top) return;
    const nav = navigation();
    let reason = '';
    if (!nav || nav.knowledgeId !== command.knowledgeId || nav.card !== command.card) reason = 'page-changed';
    else if (document.hidden || confirmationPrompt()) reason = 'confirmation-or-hidden';
    let target = null;
    if (!reason && command.action === 'goto-card') {
      if (!Number.isInteger(command.targetCard) || command.targetCard < 0 || command.targetCard >= nav.tabs.length || command.targetCard === nav.card) reason = 'card-unavailable';
      else target = nav.tabs[command.targetCard];
    }
    if (!reason && command.action === 'next-chapter') {
      if (!nav.catalogueKnown || !nav.next || nav.nextKnowledgeId !== command.targetKnowledgeId) reason = 'chapter-unavailable';
      else if (nav.nextLocked) reason = 'chapter-locked';
      else if (!nav.nextReady) reason = 'chapter-not-ready';
      else target = nav.next.querySelector('.posCatalog_name') || nav.next;
    }
    if (!reason && (!target || target.getAttribute('aria-disabled') === 'true' || /disabled/.test(target.className))) reason = 'navigation-disabled';
    if (!reason) {
      try { target.click(); } catch (_) { reason = 'navigation-click-failed'; }
    }
    emit({ kind: 'flow-result', request: command.request, ok: !reason, reason });
  }
  function stateOf(video) {
    let state = states.get(video);
    if (state) return state;
    state = { id: String(++mediaSequence), manualPause: false, pauseIntentUntil: 0, playIntentUntil: 0, playIntentVersion: -1 };
    states.set(video, state);
    video.addEventListener('pause', () => {
      if (Date.now() <= state.pauseIntentUntil) state.manualPause = true;
      queueMicrotask(sample);
    });
    video.addEventListener('playing', () => { state.manualPause = false; queueMicrotask(sample); });
    video.addEventListener('ended', () => queueMicrotask(sample));
    return state;
  }
  function requestManualPlayback(video) {
    const state = stateOf(video);
    if (!studyPaused || studyVersion < 0 || state.playIntentVersion !== studyVersion || Date.now() > state.playIntentUntil
        || !(video instanceof HTMLVideoElement) || document.hidden || !visible(video) || confirmationPrompt() || activeVideoQuiz()) return;
    state.playIntentUntil = 0;
    // Publish the current visible media first so the desktop can validate its frame.
    sample();
    emit({ kind: 'manual-play', version: studyVersion, mediaId: state.id });
  }
  function notePlaybackIntent(event) {
    if (!event.isTrusted || document.hidden || event.type === 'pointerdown' && event.button !== 0) return;
    if (event.type === 'keydown' && (event.repeat || event.ctrlKey || event.altKey || event.metaKey
        || ![' ', 'Spacebar', 'Enter', 'k', 'K'].includes(event.key))) return;
    const target = event.target instanceof Element ? event.target : null;
    if (!target) return;
    const control = target.closest('.vjs-play-control,.vjs-big-play-button,.prism-play-btn,button[aria-label="播放"],button[aria-label="Play"],button[aria-label="Play Video"],button[title="播放"],button[title="Play"]');
    const container = control && control.closest('.video-js,.prism-player');
    const video = target instanceof HTMLVideoElement ? target : container && container.querySelector('video');
    if (!video || !visible(video)) return;
    const state = stateOf(video);
    if (!video.paused) { state.pauseIntentUntil = Date.now() + 800; return; }
    if (!studyPaused) return;
    state.playIntentUntil = Date.now() + 1200;
    state.playIntentVersion = studyVersion;
    if (event.type === 'click' && control) {
      const version = studyVersion;
      // Let the official control finish its click handler before resuming the session.
      setTimeout(() => {
        if (studyVersion === version && video.paused) requestManualPlayback(video);
      }, 0);
    }
  }
  document.addEventListener('pointerdown', notePlaybackIntent, true);
  document.addEventListener('keydown', notePlaybackIntent, true);
  document.addEventListener('click', notePlaybackIntent, true);
  function emit(payload) {
    const message = Object.assign({ channel: 'study-player', token, frameId }, payload);
    if (window === top) {
      if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage(message);
    } else top.postMessage(message, '*');
  }
  function relay(command) {
    // Child documents register directly with the top page. An intermediate frame
    // without an injected monitor must not swallow playback or cancellation commands.
    if (window !== top) return;
    frameTargets.forEach((origin, target) => {
      try {
        if (target.closed || !liveFrame(target)) frameTargets.delete(target);
        else target.postMessage(command, origin);
      } catch (_) { frameTargets.delete(target); }
    });
  }
  function taskFields(rows) {
    if (!Array.isArray(rows) || rows.length > 100) throw new Error('invalid-task-list');
    return rows.map(row => {
      row = row && typeof row === 'object' ? row : {};
      const task = {}, property = row.property || {};
      for (const key of ['type', 'jobid', 'job', 'isPassed', 'playTime', 'headOffset', 'objectId']) {
        if (typeof row[key] === 'string' || typeof row[key] === 'boolean' || typeof row[key] === 'number') task[key] = row[key];
      }
      task.property = {};
      for (const key of ['objectid', 'name', 'module']) {
        if (typeof property[key] === 'string') task.property[key] = property[key].slice(0, 500);
      }
      return task;
    });
  }
  async function readCard(command) {
    const query = new URL(location.href).searchParams;
    if (!window.mArg || !Array.isArray(window.mArg.attachments) || !query.has('num') || query.get('knowledgeid') !== command.knowledgeId
        || !Number.isInteger(command.card) || command.card < 0 || command.card > 100) return;
    emit({kind: 'card-read-started', request: command.request});
    const abort = new AbortController(), timeout = setTimeout(() => abort.abort(), 8000);
    try {
      const address = new URL(location.href); address.searchParams.set('num', String(command.card));
      const response = await fetch(address.href, { credentials: 'include', cache: 'no-store', signal: abort.signal });
      if (!response.ok) throw new Error('http-' + response.status);
      if (!trusted(new URL(response.url).hostname)) throw new Error('unexpected-origin');
      const html = await response.text();
      const match = /mArg\s*=\s*(\{[\s\S]*?\})\s*;/.exec(html);
      if (!match) throw new Error('task-list-unavailable');
      const attachments = taskFields(JSON.parse(match[1]).attachments);
      emit({kind: 'card-read-result', request: command.request, ok: true, attachments});
    } catch (error) {
      emit({kind: 'card-read-result', request: command.request, ok: false,
        reason: error.name === 'AbortError' ? 'timeout' : error.name === 'SyntaxError' ? 'invalid-task-data'
          : /^(http-\d{3}|unexpected-origin|task-list-unavailable|invalid-task-list)$/.test(error.message) ? error.message : 'network'});
    } finally { clearTimeout(timeout); }
  }
  function run(command) {
    if (!command || command.channel !== 'study-player-command' || command.token !== token) return;
    if (command.action === 'read-card') { relay(command); readCard(command); return; }
    if (command.action === 'player-layout') {
      if (typeof command.expanded !== 'boolean') return;
      if (window.__studyPlayerLayout) window.__studyPlayerLayout(command.expanded, command.viewportHeight);
      relay(command);
      return;
    }
    if (command.action === 'select-media') {
      if (studyPaused || command.version !== studyVersion || typeof command.mediaId !== 'string') return;
      relay(command);
      document.querySelectorAll('video').forEach(video => {
        const state = stateOf(video);
        if (command.frameId === frameId && command.mediaId === state.id) {
          preferredMediaId = state.id; selectedVideo = video;
        } else if (!video.paused) video.pause();
      });
      if (command.frameId === frameId) {
        // Reveal the actual target player without changing its playback position.
        try {
          let node = window.frameElement;
          for (let depth = 0; node && depth < 4; depth++) {
            node.scrollIntoView({ block: 'nearest', inline: 'nearest' });
            node = node.ownerDocument.defaultView.frameElement;
          }
        } catch (_) {}
      }
      return;
    }
    if (command.action === 'study-state') {
      if (!Number.isInteger(command.version) || command.version < studyVersion || typeof command.paused !== 'boolean') return;
      const wasPaused = studyPaused;
      if (command.version !== studyVersion) document.querySelectorAll('video,audio').forEach(media => { stateOf(media).playIntentUntil = 0; });
      studyVersion = command.version;
      studyPaused = command.paused;
      if (studyPaused) {
        if (!wasPaused) quizSubmission++;
        pauseMedia();
      } else if (wasPaused) document.querySelectorAll('video,audio').forEach(media => { stateOf(media).manualPause = false; });
      relay(command);
      sample();
      return;
    }
    if (studyPaused && command.action !== 'quiz-cancel') return;
    if (command.action === 'reveal-task') {
      if (command.version !== studyVersion || document.hidden || confirmationPrompt() || activeVideoQuiz()) return;
      relay(command);
      const query = new URL(location.href).searchParams;
      if (query.get('knowledgeid') !== command.knowledgeId || Number(query.get('num')) !== command.card) return;
      const targets = Array.from(document.querySelectorAll('iframe[data]')).filter(frame => {
        let data; try { data = JSON.parse(frame.getAttribute('data')); } catch (_) { return false; }
        const job = String(data.jobid || data._jobid || ''), object = String(data.objectid || '');
        if (command.jobId && job && command.jobId !== job || command.objectId && object && command.objectId !== object) return false;
        return command.jobId && command.jobId === job || command.objectId && command.objectId === object;
      });
      if (targets.length === 1) targets[0].scrollIntoView({ block: 'center', inline: 'nearest' });
      return;
    }
    if (command.action === 'goto-card' || command.action === 'next-chapter') { navigate(command); return; }
    if (command.action === 'resume' && (confirmationPrompt() || activeVideoQuiz())) {
      emit({ kind: 'resume-result', attempt: command.attempt, targetFrame: command.frameId,
        ok: false, reason: 'confirmation' });
      return;
    }
    if (command.action === 'play' && activeVideoQuiz()) {
      if (selectedVideo) stateOf(selectedVideo).manualPause = false;
      sample(); return;
    }
    relay(command);
    if (command.frameId === frameId && command.action.startsWith('quiz-')) { quizCommand(command); return; }
    if (command.frameId !== frameId) return;
    const video = command.mediaId ? Array.from(document.querySelectorAll('video')).find(item => stateOf(item).id === command.mediaId) : selectedVideo;
    if (!video) return;
    const state = stateOf(video);
    if (command.action === 'pause') { state.manualPause = true; video.pause(); sample(); }
    if (command.action === 'play' || command.action === 'resume') {
      const automatic = command.action === 'resume';
      if (automatic && (state.manualPause || video.ended || document.hidden || video.error)) {
        emit({ kind: 'resume-result', attempt: command.attempt, targetFrame: frameId,
          ok: false, reason: state.manualPause ? 'manual' : video.ended ? 'ended' : document.hidden ? 'hidden' : 'media-error' });
        return;
      }
      if (!automatic) state.manualPause = false;
      const failed = error => {
        if (automatic) emit({ kind: 'resume-result', attempt: command.attempt, targetFrame: frameId,
          ok: false, reason: error && error.name === 'NotAllowedError' ? 'interaction-required' : 'play-failed' });
        else emit({ kind: 'play-error', message: '请在官方播放器中点击播放，或完成页面上的观看确认。' });
      };
      try {
        Promise.resolve(video.play()).then(() => {
          if (automatic) emit({ kind: 'resume-result', attempt: command.attempt, targetFrame: frameId, ok: true });
        }, failed);
      } catch (error) { failed(error); }
    }
  }
  function liveFrame(target, root = window, depth = 0) {
    if (target === root) return true;
    if (depth > 8) return false;
    try { for (let i = 0; i < Math.min(root.frames.length, 200); i++) if (liveFrame(target, root.frames[i], depth + 1)) return true; } catch (_) {}
    return false;
  }
  window.addEventListener('message', event => {
    let origin;
    try { origin = new URL(event.origin); } catch (_) { return; }
    if (!trusted(origin.hostname)) return;
    const data = event.data;
    if (window === top && data && data.channel === 'study-player' && data.token === token) {
      if (!liveFrame(event.source)) return;
      frameTargets.set(event.source, event.origin);
      window.chrome.webview.postMessage(data);
    }
    if (window !== top && (event.source === top || event.source === parent)) run(data);
  });
  if (window === top && window.chrome && window.chrome.webview) {
    window.chrome.webview.addEventListener('message', event => run(event.data));
  }
  function visible(element) {
    const rect = element.getBoundingClientRect();
    const style = getComputedStyle(element);
    return rect.width > 0 && rect.height > 0 && style.display !== 'none' && style.visibility !== 'hidden';
  }
  function confirmationPrompt() {
    let prompt = '';
    document.querySelectorAll('[role="dialog"],[role="alertdialog"],.layui-layer,.vjs-modal-dialog,.ans-timelineobjects,.maskDiv,.popDiv,.verify-container,.captcha-dialog,#validate,#verifyCodeTipWin').forEach(dialog => {
      if (!visible(dialog)) return;
      const text = (dialog.innerText || '').replace(/\s+/g, ' ').trim();
      if (dialog.matches('.ans-timelineobjects') && dialog.querySelector('.ans-videoquiz') && !/验证码|人脸|身份验证|请.*验证/.test(text)) return;
      if (/继续观看|继续播放|观看确认|验证码|人脸|身份验证|长时间未操作|请.*验证/.test(text)) prompt = text.slice(0, 160);
    });
    return prompt;
  }
  function mediaIdentity(video) {
    let jobId = video.getAttribute('data-job-id') || '', objectId = video.getAttribute('data-object-id') || '';
    let knowledgeId = '', cardNum = -1;
    let current = window;
    for (let depth = 0; current && depth < 5; depth++) {
      try {
        const query = new URL(current.location.href).searchParams;
        const defaults = current.mArg && current.mArg.defaults || {};
        if (!knowledgeId) knowledgeId = query.get('knowledgeid') || query.get('chapterId') || defaults.knowledgeid || '';
        if (cardNum < 0 && query.has('num')) cardNum = Number(query.get('num'));
        jobId = jobId || query.get('jobid') || query.get('jobId') || '';
        objectId = objectId || query.get('objectid') || query.get('objectId') || '';
        const element = current.frameElement;
        if (element) {
          let data = {};
          try { data = JSON.parse(element.getAttribute('data') || '{}'); } catch (_) {}
          jobId = jobId || data.jobid || data._jobid || element.getAttribute('jobid') || '';
          objectId = objectId || data.objectid || element.getAttribute('objectid') || '';
        }
        if (current === top) break;
        current = current.parent;
      } catch (_) { break; }
    }
    return { jobId: String(jobId), objectId: String(objectId), knowledgeId: String(knowledgeId), cardNum };
  }
  function repairMonitors(root, depth = 0) {
    if (depth > 8) return;
    // Dynamic same-origin subframes can finish loading without the document-created
    // hook. Repair only missing monitors, with a limit for documents blocking scripts.
    root.querySelectorAll('iframe').forEach(frame => {
      try {
        const child = frame.contentWindow, doc = frame.contentDocument;
        if (!child || !doc || !doc.documentElement || child.__studyPlayerMonitor) return;
        // about:blank bridges inherit access to their parent but have no trusted host;
        // leave them unmonitored while reaching trusted player documents below them.
        if (!/^https?:$/.test(child.location.protocol) || !trusted(child.location.hostname)) { repairMonitors(doc, depth + 1); return; }
        const repair = monitorRepairs.get(doc) || { count: 0, at: 0 };
        if (repair.count >= 3 || Date.now() - repair.at < 5000) return;
        repair.count++; repair.at = Date.now(); monitorRepairs.set(doc, repair);
        const script = doc.createElement('script'); script.textContent = '(' + installStudyPlayerMonitor.toString() + ')();';
        doc.documentElement.appendChild(script); script.remove();
      } catch (_) {}
    });
  }
  function sample() {
    repairMonitors(document);
    if (studyPaused) pauseMedia();
    const nav = navigation();
    if (nav) emit({ kind: 'navigation', knowledgeId: nav.knowledgeId, cardNum: nav.card,
      cardCount: nav.tabs.length, cardTitle: nav.tabs[nav.card].innerText.trim().replace(/^\d+\s*/, '').slice(0, 60),
      nextKnowledgeId: nav.nextKnowledgeId, nextLocked: nav.nextLocked, nextReady: nav.nextReady, catalogueKnown: nav.catalogueKnown,
      pageVisible: !document.hidden });
    const query = new URL(location.href).searchParams;
    const defaults = window.mArg && window.mArg.defaults || {};
    const knowledgeId = query.get('knowledgeid') || query.get('chapterId') || defaults.knowledgeid;
    const courseId = query.get('courseid') || query.get('courseId') || defaults.courseid;
    if (knowledgeId) emit({ kind: 'context', knowledgeId: String(knowledgeId),
      courseId: courseId ? String(courseId) : '', cardNum: query.has('num') ? Number(query.get('num')) : -1 });
    // Reuse the task structure already loaded by the official page. Never relay
    // defaults, answer keys, signed URLs, cookies or the complete mArg object.
    if (knowledgeId && query.has('num') && window.mArg && Array.isArray(window.mArg.attachments) && window.mArg.attachments.length <= 100) {
      const attachments = taskFields(window.mArg.attachments);
      const signature = JSON.stringify(attachments);
      if (signature !== lastTaskPlan || Date.now() - lastTaskPlanAt > 5000) {
        lastTaskPlan = signature; lastTaskPlanAt = Date.now();
        emit({kind: 'task-plan', knowledgeId: String(knowledgeId), cardNum: Number(query.get('num')), attachments});
      }
    }
    const prompt = confirmationPrompt();
    emit({ kind: 'prompt', prompt });
    const quiz = quizPage();
    if (quiz) emit({ kind: 'quiz-page', fingerprint: quiz.fingerprint, total: quiz.questions.length, pageVisible: !document.hidden });
    const videoQuiz = videoQuizPage();
    if (videoQuiz) emit({ kind: 'video-quiz-page', mode: 'video', fingerprint: videoQuiz.fingerprint, total: 1,
      accepted: videoFeedback(videoQuiz.root, '#spanHas'), canContinueRejected: Boolean(canContinueRejected(videoQuiz)), pageVisible: !document.hidden });
    else if (sampledVideoQuiz) emit({ kind: 'video-quiz-gone' });
    sampledVideoQuiz = Boolean(videoQuiz);
    const videos = Array.from(document.querySelectorAll('video')).filter(visible);
    selectedVideo = videos.find(video => stateOf(video).id === preferredMediaId) || videos.find(video => !video.paused && !video.ended) || videos[0] || null;
    if (!selectedVideo) return;
    for (const video of videos) {
    const state = stateOf(video);
    emit(Object.assign({ kind: 'media', mediaId: state.id, position: Number.isFinite(video.currentTime) ? video.currentTime : 0,
      duration: Number.isFinite(video.duration) ? video.duration : 0, paused: video.paused,
      ended: video.ended, readyState: video.readyState, error: video.error ? video.error.code : 0,
      manualPause: state.manualPause, pageVisible: !document.hidden,
      hasSource: Boolean(video.currentSrc || video.getAttribute('src') || video.querySelector('source[src]')),
      title: document.title || '', prompt }, mediaIdentity(video)));
    }
  }
  setInterval(sample, 1000);
  document.addEventListener('DOMContentLoaded', sample);
  emit({ kind: 'player-ready' });
  sample();
})();

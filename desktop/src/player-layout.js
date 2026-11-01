(() => {
  'use strict';
  const test = __PLAYER_TEST__;
  const trusted = location.hostname === 'chaoxing.com' || location.hostname.endsWith('.chaoxing.com') ||
    (test && location.hostname === 'player-test.invalid');
  if (!trusted || window.__studyPlayerLayout) return;
  const path = location.pathname;
  const kind = /\/mycourse\/studentstudy\/?$/i.test(path) || (test && path === '/layout-study.html') ? 'study' :
    /\/knowledge\/cards\/?$/i.test(path) || (test && path === '/layout-cards.html') ? 'cards' :
    /\/ananas\/modules\/video\/index\.html$/i.test(path) || (test && path === '/layout-video.html') ? 'video' : '';
  if (!kind) return;
  let expanded = false;
  let viewportHeight = 0;
  const css = `
    html[data-study-layout] { min-width:0 !important; }
    html[data-study-layout] body { min-width:0 !important; width:100% !important; box-sizing:border-box !important; }
    html[data-study-layout] .writeNote_vid_con { display:none !important; }
    html[data-study-layout="study"] { --study-side:0px; --study-catalogue:360px; --study-tabs-height:80px; }
    html[data-study-layout="study"][data-study-catalogue="open"] { --study-side:var(--study-catalogue); }
    html[data-study-layout="study"] body > .left,
    html[data-study-layout="study"] .left > .content {
      width:100% !important; min-width:0 !important; height:auto !important;
      margin:0 !important; position:relative !important;
    }
    html[data-study-layout="study"] .subNav,
    html[data-study-layout="study"] .iconBox { display:none !important; }
    html[data-study-layout="study"] body { background:#faf9f6 !important; }
    html[data-study-layout="study"] .left > .content { background:transparent !important; }
    html[data-study-layout="study"] #mainid {
      width:100% !important; min-width:0 !important; box-sizing:border-box !important;
      margin:0 0 24px !important; padding:8px calc(var(--study-side) + 16px) 16px 16px !important;
    }
    html[data-study-layout="study"] #mainid > h1 { font-size:20px !important; color:#423568 !important; padding:10px 12px !important; margin:0 !important; }
    html[data-study-layout="study"] #mainid .prev_title_pos {
      position:static !important; width:100% !important; height:auto !important; min-width:0 !important;
      padding:16px 12px 12px !important; margin:0 !important; box-sizing:border-box !important;
    }
    html[data-study-layout="study"] #mainid .prev_title {
      width:100% !important; max-width:100% !important; height:auto !important; padding:0 !important; margin:0 !important;
      color:#3b3150 !important; font-size:22px !important; font-weight:600 !important; line-height:1.5 !important;
      white-space:normal !important; overflow-wrap:anywhere !important; text-align:left !important; box-sizing:border-box !important;
    }
    html[data-study-layout="study"] #mainid .prev_list {
      width:100% !important; height:auto !important; min-width:0 !important;
      margin:0 !important; padding:0 12px 16px !important; box-sizing:border-box !important;
    }
    html[data-study-layout="study"] #mainid .prev_ul {
      display:flex !important; flex-wrap:wrap !important; align-items:center !important; gap:10px !important;
      width:100% !important; min-width:0 !important; height:auto !important; margin:0 !important; padding:0 !important;
      list-style:none !important; overflow:visible !important; box-sizing:border-box !important;
    }
    html[data-study-layout="study"] #mainid .prev_ul::before,
    html[data-study-layout="study"] #mainid .prev_ul::after { display:none !important; }
    html[data-study-layout="study"] #mainid .prev_ul > li {
      flex:0 1 auto !important; float:none !important; min-width:0 !important; max-width:100% !important; width:auto !important;
      height:auto !important; margin:0 !important; padding:0 !important; border:0 !important; background:none !important;
      box-sizing:border-box !important; cursor:pointer !important;
    }
    html[data-study-layout="study"] #mainid .prev_ul .prev_white {
      display:flex !important; align-items:center !important; gap:8px !important;
      width:auto !important; min-width:0 !important; max-width:100% !important; min-height:40px !important; height:auto !important;
      margin:0 !important; padding:8px 12px !important; border:1px solid #e8e1f1 !important; border-radius:12px !important;
      background:#f4f1f8 !important; color:#756585 !important; font-size:14px !important; line-height:22px !important;
      white-space:normal !important; box-sizing:border-box !important;
    }
    html[data-study-layout="study"] #mainid .prev_ul .num {
      display:inline-flex !important; align-items:center !important; justify-content:center !important; flex:0 0 22px !important;
      width:22px !important; height:22px !important; margin:0 !important; padding:0 !important; border-radius:7px !important;
      background:#eae3f3 !important; color:#71579c !important; font-size:12px !important; line-height:22px !important;
    }
    html[data-study-layout="study"] #mainid .prev_ul .spanText {
      display:block !important; min-width:0 !important; height:auto !important; line-height:22px !important;
      white-space:normal !important; overflow-wrap:anywhere !important;
    }
    html[data-study-layout="study"] #mainid .prev_ul > li:hover .prev_white { background:#ece5f5 !important; }
    html[data-study-layout="study"] #mainid .prev_ul > li.active .prev_white {
      background:#7656b0 !important; border-color:#7656b0 !important; color:#fff !important; box-shadow:0 3px 8px #7656b021 !important;
    }
    html[data-study-layout="study"] #mainid .prev_ul > li.active .num { color:#fff !important; background:#ffffff26 !important; }
    html[data-study-layout="study"] #mainid .course_main,
    html[data-study-layout="study"] iframe#iframe {
      display:block !important; width:100% !important; min-width:0 !important; max-width:100% !important;
      box-sizing:border-box !important; margin-left:0 !important; margin-right:0 !important;
    }
    html[data-study-layout="study"] #selector { display:none !important; }
    html[data-study-layout="study"][data-study-catalogue="open"] #selector {
      display:block !important; position:fixed !important; left:auto !important; right:0 !important;
      top:0 !important; bottom:0 !important; width:var(--study-catalogue) !important;
      height:100vh !important; min-width:0 !important; transform:none !important;
      border-left:1px solid #e4dff1; box-sizing:border-box !important; background:#fff !important;
    }
    html[data-study-layout="study"] #selector .tabs {
      height:var(--study-tabs-height) !important; padding-top:16px !important; box-sizing:border-box !important;
      background:#fff !important; border:0 !important;
    }
    /* The official header contains a list and a clearing div. Style the list itself. */
    html[data-study-layout="study"] #selector .tabs > .tabCatalog {
      display:block !important; width:calc(100% - 32px) !important; height:48px !important;
      margin:0 16px !important; padding:4px !important; box-sizing:border-box !important;
      border:1px solid #e8e3f0 !important; border-radius:14px !important; background:#f3f0f8 !important;
    }
    html[data-study-layout="study"] #selector .tabCatalog > ul {
      display:grid !important; grid-template-columns:repeat(4,minmax(0,1fr)) !important; gap:6px !important;
      width:100% !important; height:100% !important; min-width:0 !important; float:none !important;
      margin:0 !important; padding:0 !important; border:0 !important; list-style:none !important; box-sizing:border-box !important;
    }
    html[data-study-layout="study"] #selector .tabCatalog > ul > li {
      display:flex !important; align-items:center !important; justify-content:center !important; gap:3px !important;
      position:relative !important; float:none !important; min-width:0 !important; width:auto !important; height:100% !important;
      margin:0 !important; padding:0 4px !important; border:0 !important; border-radius:10px !important;
      font-family:"Microsoft YaHei UI","Microsoft YaHei",sans-serif !important; font-size:14px !important;
      font-weight:500 !important; line-height:1.3 !important; color:#736b82 !important; text-align:center !important;
      white-space:nowrap !important; box-sizing:border-box !important; background:transparent !important;
      cursor:pointer !important; transition:background-color .15s,color .15s,box-shadow .15s !important;
    }
    html[data-study-layout="study"] #selector .tabCatalog > ul > li:hover { background:#e9e3f2 !important; color:#5c438e !important; }
    html[data-study-layout="study"] #selector .tabCatalog > ul > li.active {
      background:#fff !important; color:#6a4da0 !important; font-weight:600 !important;
      box-shadow:0 2px 6px #4a33651a !important;
    }
    html[data-study-layout="study"] #selector .tabCatalog > ul > li::before,
    html[data-study-layout="study"] #selector .tabCatalog > ul > li::after,
    html[data-study-layout="study"] #selector .tabCatalog > .clear { display:none !important; }
    html[data-study-layout="study"] #selector .tabCatalog .discussNum:empty { display:none !important; }
    html[data-study-layout="study"] #selector .showcontent {
      left:auto !important; right:0 !important; top:var(--study-tabs-height) !important;
      width:var(--study-catalogue) !important; height:calc(100vh - var(--study-tabs-height)) !important;
      min-width:0 !important; box-sizing:border-box !important;
    }
    html[data-study-layout="study"] #selector #content1 { width:100% !important; height:100% !important; box-sizing:border-box !important; }
    html[data-study-layout="study"] #selector #coursetree { width:100% !important; height:calc(100vh - var(--study-tabs-height) - 60px) !important; box-sizing:border-box !important; overflow-y:auto !important; }
    html[data-study-layout="study"] #selector .drag,
    html[data-study-layout="study"] #selector .switchbtn { display:none !important; }
    /* The official assistant's floating promotion also covers the player. */
    html[data-study-layout="study"] .cx-robot-wrapper { display:none !important; }
    @media (max-width:850px) { html[data-study-layout="study"] { --study-catalogue:320px; } }
    html[data-study-layout="cards"] body { padding:12px !important; margin:0 !important; }
    html[data-study-layout="cards"] body > .wrap,
    html[data-study-layout="cards"] #pageDiv {
      width:100% !important; min-width:0 !important; max-width:100% !important;
      padding:0 !important; margin:0 !important; box-sizing:border-box !important;
    }
    html[data-study-layout="cards"] .ans-attach-ct {
      width:100% !important; min-width:0 !important; max-width:100% !important;
      box-sizing:border-box !important; margin-left:0 !important; margin-right:0 !important;
    }
    html[data-study-layout="cards"] .ans-attach-ct > iframe {
      width:100% !important; min-width:0 !important; max-width:100% !important; box-sizing:border-box !important;
    }
    html[data-study-layout="cards"] iframe.ans-insertvideo-online { border:0 !important; border-radius:10px; display:block !important; }
    html[data-study-layout="cards"] .ans-job-video {
      display:flex !important; flex-direction:column !important; align-items:flex-start !important; gap:10px !important;
      position:relative !important; width:100% !important; height:auto !important; min-height:22px !important;
      margin:0 0 16px !important; padding:0 !important; box-sizing:border-box !important;
    }
    html[data-study-layout="cards"] .ans-job-video > i { flex:none !important; }
    html[data-study-layout="cards"] .ans-job-video .task-condition {
      display:block !important; position:static !important; left:auto !important; top:auto !important; transform:none !important;
      width:100% !important; max-width:100% !important; min-width:0 !important; height:auto !important; min-height:44px !important;
      margin:0 !important; padding:10px 12px !important; box-sizing:border-box !important;
      border:1px solid #e5e9f1 !important; border-radius:10px !important; background:#f5f7fb !important;
      color:#7b8495 !important; font-size:12px !important; line-height:1.8 !important;
      white-space:normal !important; overflow-wrap:anywhere !important;
    }
    html[data-study-layout="cards"] .ans-job-video .task-condition .special-text {
      display:inline !important; padding:0 !important; margin-right:8px !important;
      font-family:inherit !important; color:#59677e !important; font-weight:600 !important; white-space:nowrap !important;
    }
    html[data-study-layout="cards"] .ans-job-video .task-condition .special-text::after { display:none !important; }
    html[data-study-layout="video"] .main,
    html[data-study-layout="video"] .prev_video_left,
    html[data-study-layout="video"] .fullScreenContainer,
    html[data-study-layout="video"] #reader { width:100% !important; min-width:0 !important; max-width:100% !important; box-sizing:border-box !important; }
    html[data-study-layout="video"] .fullScreenContainer,
    html[data-study-layout="video"] #reader { height:auto !important; }
    html[data-study-layout="video"] #video:not(.vjs-fullscreen) {
      width:100% !important; height:auto !important; aspect-ratio:var(--study-video-ratio,16 / 9);
      max-height:var(--study-video-max-height,none) !important;
    }
    html[data-study-layout="video"] #video > video { width:100% !important; height:100% !important; object-fit:contain !important; }
    html[data-study-layout="video"] #video .ans-timelineobjects { width:100% !important; height:100% !important; max-width:100% !important; box-sizing:border-box !important; }
    html[data-study-layout="video"] #video.vjs-fullscreen,
    html[data-study-layout="video"] #video:fullscreen { width:100% !important; height:100% !important; max-height:none !important; }
  `;
  function apply() {
    const root = document.documentElement;
    if (!root) return;
    root.dataset.studyLayout = kind;
    root.dataset.studyCatalogue = expanded ? 'open' : 'closed';
    if (viewportHeight > 0) root.style.setProperty('--study-video-max-height', Math.max(180, viewportHeight - 160) + 'px');
    if (!document.getElementById('study-desktop-layout')) {
      const style = document.createElement('style');
      style.id = 'study-desktop-layout'; style.textContent = css;
      (document.head || root).appendChild(style);
    }
  }
  window.__studyPlayerLayout = (value, height) => {
    expanded = value;
    if (Number.isFinite(height) && height > 0) viewportHeight = height;
    apply();
  };
  if (kind === 'video') document.addEventListener('loadedmetadata', event => {
    const video = event.target;
    if (video instanceof HTMLVideoElement && video.videoWidth && video.videoHeight)
      document.documentElement.style.setProperty('--study-video-ratio', video.videoWidth + ' / ' + video.videoHeight);
  }, true);
  apply();
  document.addEventListener('DOMContentLoaded', apply, { once:true });
})();

(function () {
  'use strict';

  const root = document.documentElement;
  const content = document.getElementById('content');
  const toc = document.getElementById('toc');
  const pending = [];
  const errors = [];
  window.addEventListener('error', e => errors.push(String(e.message || e)));

  function store(key, value) {
    try { if (value === undefined) return localStorage.getItem(key); localStorage.setItem(key, value); } catch (e) { return null; }
  }

  // ---------- tema ----------
  function isDark() {
    const t = root.dataset.theme;
    return t === 'dark' || (t !== 'light' && matchMedia('(prefers-color-scheme: dark)').matches);
  }
  function cycleTheme() {
    const next = { auto: isDark() ? 'light' : 'dark', dark: 'light', light: 'dark' }[root.dataset.theme || 'auto'];
    root.dataset.theme = next;
    store('mdv-theme', next);
    postTheme();
    renderMermaid();
  }
  function postTheme() { window.chrome?.webview?.postMessage('theme:' + (isDark() ? 'dark' : 'light')); }

  // ---------- indice ----------
  function buildToc() {
    const heads = content.querySelectorAll('h1[id], h2[id], h3[id]');
    if (heads.length < 2) { document.getElementById('btn-toc').hidden = true; return; }
    const ul = document.createElement('ul');
    heads.forEach(h => {
      const li = document.createElement('li');
      li.className = 'toc-' + h.tagName.toLowerCase();
      const a = document.createElement('a');
      a.href = '#' + h.id;
      a.textContent = h.textContent;
      li.appendChild(a);
      ul.appendChild(li);
    });
    toc.appendChild(ul);
    setToc(store('mdv-toc') !== '0');
  }
  function setToc(visible) {
    toc.hidden = !visible;
    document.body.classList.toggle('with-toc', visible);
    store('mdv-toc', visible ? '1' : '0');
  }

  // ---------- Mermaid (libreria locale) ----------
  const mermaidBlocks = [];
  function collectMermaid() {
    content.querySelectorAll('pre > code.language-mermaid').forEach(code => {
      const div = document.createElement('div');
      div.className = 'mermaid';
      div.textContent = code.textContent;
      code.parentElement.replaceWith(div);
    });
    content.querySelectorAll('.mermaid').forEach(el => mermaidBlocks.push({ el, src: el.textContent }));
  }
  function renderMermaid() {
    if (!mermaidBlocks.length || !window.mermaid) return Promise.resolve();
    mermaidBlocks.forEach(b => { b.el.removeAttribute('data-processed'); b.el.textContent = b.src; });
    window.mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', theme: isDark() ? 'dark' : 'default' });
    return window.mermaid.run({ nodes: mermaidBlocks.map(b => b.el), suppressErrors: true })
      .catch(e => errors.push('mermaid: ' + e));
  }

  // ---------- PlantUML (server plantuml.com) ----------
  const PUML_ALPHABET = '0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz-_';
  function encode64(bytes) {
    let out = '';
    for (let i = 0; i < bytes.length; i += 3) {
      const b1 = bytes[i], b2 = i + 1 < bytes.length ? bytes[i + 1] : 0, b3 = i + 2 < bytes.length ? bytes[i + 2] : 0;
      out += PUML_ALPHABET[b1 >> 2] + PUML_ALPHABET[((b1 & 0x3) << 4) | (b2 >> 4)] +
             PUML_ALPHABET[((b2 & 0xF) << 2) | (b3 >> 6)] + PUML_ALPHABET[b3 & 0x3F];
    }
    return out;
  }
  async function deflateRaw(text) {
    const stream = new Blob([new TextEncoder().encode(text)]).stream().pipeThrough(new CompressionStream('deflate-raw'));
    return new Uint8Array(await new Response(stream).arrayBuffer());
  }
  // I diagrammi PlantUML si inviano a plantuml.com solo se l'utente lo sceglie (data-plantuml="always")
  // o se clicca "Mostra diagramma": il testo del diagramma esce dal PC.
  function plantUmlBlocks() {
    return [...content.querySelectorAll('pre > code.language-plantuml, pre > code.language-puml, pre > code.language-uml')];
  }
  async function showPlantUml(code) {
    let src = code.textContent.trim();
    if (!/^@start/m.test(src)) src = '@startuml\n' + src + '\n@enduml';
    const pre = code.closest('pre');
    const holder = pre.closest('figure.plantuml') || pre;
    const fig = document.createElement('figure');
    fig.className = 'plantuml';
    const img = document.createElement('img');
    img.alt = 'Diagramma PlantUML';
    img.addEventListener('error', () => {
      fig.classList.add('failed');
      fig.title = 'Rendering PlantUML non riuscito (server plantuml.com non raggiungibile?)';
      fig.replaceChildren(pre.cloneNode(true));
    });
    pending.push(new Promise(res => { img.addEventListener('load', res); img.addEventListener('error', res); }));
    img.src = 'https://www.plantuml.com/plantuml/svg/' + encode64(await deflateRaw(src));
    fig.appendChild(img);
    holder.replaceWith(fig);
  }
  function offerPlantUml(code) {
    const pre = code.parentElement;
    const fig = document.createElement('figure');
    fig.className = 'plantuml ask';
    const bar = document.createElement('div');
    bar.className = 'plantuml-ask';
    const btn = document.createElement('button');
    btn.textContent = 'Mostra diagramma';
    btn.title = 'Invia il testo del diagramma al server pubblico plantuml.com';
    const label = document.createElement('label');
    const remember = document.createElement('input');
    remember.type = 'checkbox';
    label.append(remember, ' usa sempre plantuml.com');
    const note = document.createElement('span');
    note.className = 'plantuml-note';
    note.textContent = 'Il testo del diagramma verrà inviato a plantuml.com.';
    bar.append(btn, label, note);
    pre.replaceWith(fig);
    fig.append(bar, pre);
    btn.addEventListener('click', () => {
      if (remember.checked) {
        window.chrome?.webview?.postMessage('plantuml:always');
        root.dataset.plantuml = 'always';
        plantUmlBlocks().forEach(c => showPlantUml(c));
      } else showPlantUml(code);
    });
  }
  async function renderPlantUml() {
    const always = root.dataset.plantuml === 'always';
    for (const code of plantUmlBlocks()) {
      if (always) await showPlantUml(code); else offerPlantUml(code);
    }
  }

  // ---------- ancore: id esatto, altrimenti confronto col testo dei titoli (accenti, numerazione) ----------
  function normalize(s) {
    return s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase().replace(/^[\d.\s]+/, '').replace(/[^a-z0-9]+/g, ' ').trim();
  }
  function scrollToHash() {
    if (!location.hash) return false;
    let h;
    try { h = decodeURIComponent(location.hash.slice(1)); } catch (e) { h = location.hash.slice(1); }
    let el = document.getElementById(h);
    if (!el) {
      const t = normalize(h);
      el = [...content.querySelectorAll('h1,h2,h3,h4,h5,h6')].find(x => normalize(x.textContent) === t || normalize(x.id) === t);
    }
    if (el) { el.scrollIntoView(); return true; }
    return false;
  }
  addEventListener('hashchange', scrollToHash);

  // ---------- scroll conservato alla ricarica automatica ----------
  window.__mdvSaveScroll = () => { try { sessionStorage.setItem('mdv-scroll', JSON.stringify({ p: location.pathname, y: scrollY })); } catch (e) { } };
  function restoreScroll() {
    try {
      const s = JSON.parse(sessionStorage.getItem('mdv-scroll') || 'null');
      sessionStorage.removeItem('mdv-scroll');
      if (s && s.p === location.pathname) { scrollTo(0, s.y); return true; }
    } catch (e) { }
    return false;
  }

  // ---------- comandi ----------
  document.getElementById('btn-toc').addEventListener('click', () => setToc(toc.hidden));
  document.getElementById('btn-theme').addEventListener('click', cycleTheme);
  document.addEventListener('keydown', e => {
    if (e.ctrlKey && (e.key === 'o' || e.key === 'O')) { e.preventDefault(); window.chrome?.webview?.postMessage('open'); return; }
    if (e.ctrlKey && (e.key === 'b' || e.key === 'B')) { e.preventDefault(); window.chrome?.webview?.postMessage('toggle-tree'); return; }
    if (e.ctrlKey || e.altKey || e.metaKey || /input|textarea/i.test(e.target.tagName)) return;
    if (e.key === 't' || e.key === 'T') setToc(toc.hidden);
    if (e.key === 'd' || e.key === 'D') cycleTheme();
  });
  matchMedia('(prefers-color-scheme: dark)').addEventListener('change', () => { if (root.dataset.theme === 'auto') { postTheme(); renderMermaid(); } });

  // ---------- avvio ----------
  let ready = false;
  postTheme();
  buildToc();
  collectMermaid();
  Promise.all([renderMermaid(), renderPlantUml()])
    .then(() => Promise.all(pending))
    .finally(() => { if (!restoreScroll()) scrollToHash(); ready = true; });

  window.__mdvReport = () => ({
    ready,
    title: document.title,
    headings: content.querySelectorAll('h1,h2,h3,h4').length,
    tables: content.querySelectorAll('table').length,
    tocEntries: toc.querySelectorAll('li').length,
    mermaidBlocks: mermaidBlocks.length,
    mermaidSvg: content.querySelectorAll('.mermaid svg').length,
    plantumlImgs: content.querySelectorAll('figure.plantuml img').length,
    plantumlAsk: content.querySelectorAll('figure.plantuml.ask').length,
    scrolledTo: scrollY,
    plantumlLoaded: [...content.querySelectorAll('figure.plantuml img')].filter(i => i.complete && i.naturalWidth > 0).length,
    plantumlFailed: content.querySelectorAll('figure.plantuml.failed').length,
    wikilinks: [...content.querySelectorAll('a[href*="/f/"]')].length,
    wikilinksMissing: content.querySelectorAll('.wikilink-missing').length,
    taskItems: content.querySelectorAll('input[type=checkbox]').length,
    frontMatter: !!content.querySelector('.front-matter'),
    firstHeading: (content.querySelector('h1') || {}).textContent || null,
    errors,
  });
})();

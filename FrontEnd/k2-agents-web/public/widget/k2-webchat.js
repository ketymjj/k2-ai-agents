/*!
 * K2 AI Agents — Web Chat widget
 * Uso: <script src="https://SEU-DOMINIO/widget/k2-webchat.js" data-token="wc_..." async></script>
 *
 * Segurança: nenhum HTML vindo do servidor é interpretado (apenas textContent); estilos isolados em Shadow DOM;
 * o token público só identifica o canal; a conversa é protegida por uma chave aleatória guardada neste navegador.
 */
(function () {
  'use strict';

  var script = document.currentScript || document.querySelector('script[src*="k2-webchat"][data-token]');
  if (!script || window.__k2WebChatLoaded) return;
  window.__k2WebChatLoaded = true;

  var token = script.getAttribute('data-token');
  if (!token) return console.warn('[K2 Web Chat] atributo data-token ausente.');

  var base = new URL(script.src, location.href).origin;
  var api = base + '/api/v1/public/webchat';
  var storageKey = 'k2wc:' + token;

  var TEXT = {
    'pt-BR': {
      open: 'Abrir chat', close: 'Fechar chat', placeholder: 'Digite sua mensagem…', send: 'Enviar',
      simulated: 'Modo demonstração: respostas simuladas, sem inteligência artificial.', simulatedBadge: 'RESPOSTA SIMULADA',
      unavailable: 'Chat indisponível no momento.', error: 'Não foi possível enviar. Tente novamente.',
      typing: 'digitando', powered: 'K2 AI Agents', online: 'Online', attendant: 'ATENDENTE'
    },
    en: {
      open: 'Open chat', close: 'Close chat', placeholder: 'Type your message…', send: 'Send',
      simulated: 'Demo mode: simulated replies, no artificial intelligence.', simulatedBadge: 'SIMULATED REPLY',
      unavailable: 'Chat unavailable right now.', error: 'Could not send. Please try again.',
      typing: 'typing', powered: 'K2 AI Agents', online: 'Online', attendant: 'ATTENDANT'
    }
  };

  var state = { config: null, session: null, messages: {}, order: [], open: false, ready: false, sending: false, lastAt: null, poll: null, hub: null };
  var t = TEXT['pt-BR'];

  // ---------- Armazenamento (pode estar bloqueado: o chat continua funcionando sem ele) ----------
  function loadSession() {
    try { return JSON.parse(localStorage.getItem(storageKey) || 'null'); } catch (e) { return null; }
  }
  function saveSession(s) {
    try { s ? localStorage.setItem(storageKey, JSON.stringify(s)) : localStorage.removeItem(storageKey); } catch (e) { /* ignora */ }
  }

  // ---------- HTTP ----------
  function request(method, path, body) {
    // Erros do servidor no idioma do agente (antes da configuração chegar, o do navegador).
    var headers = { 'Accept-Language': (state.config && state.config.language) || navigator.language || 'pt-BR' };
    if (body) headers['Content-Type'] = 'application/json';
    return fetch(api + path, {
      method: method,
      headers: headers,
      body: body ? JSON.stringify(body) : undefined,
      credentials: 'omit'
    }).then(function (res) {
      return res.json().catch(function () { return {}; }).then(function (data) {
        if (!res.ok) { var err = new Error(data.title || res.status); err.status = res.status; err.code = data.code; throw err; }
        return data;
      });
    });
  }

  // ---------- DOM ----------
  function el(tag, cls, text) {
    var node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text != null) node.textContent = text;
    return node;
  }

  var host = el('div');
  host.id = 'k2-webchat';
  var root = host.attachShadow ? host.attachShadow({ mode: 'open' }) : host;
  var style = el('style');
  var launcher = el('button', 'launcher');
  var panel = el('section', 'panel');
  var header = el('header', 'header');
  var titleBox = el('div', 'titles');
  var titleEl = el('strong', 'title');
  var subtitleEl = el('span', 'subtitle');
  var closeBtn = el('button', 'close', '×');
  var notice = el('div', 'notice');
  var list = el('div', 'messages');
  var typing = el('div', 'msg agent typing');
  var form = el('form', 'composer');
  var input = el('textarea');
  var sendBtn = el('button', 'send');
  var footer = el('div', 'footer');

  launcher.type = 'button';
  launcher.innerHTML = '<svg viewBox="0 0 24 24" width="28" height="28" aria-hidden="true"><path fill="currentColor" d="M4 4h16a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H8l-4 4V6a2 2 0 0 1 2-2z"/></svg>';
  closeBtn.type = 'button';
  sendBtn.type = 'submit';
  sendBtn.innerHTML = '<svg viewBox="0 0 24 24" width="20" height="20" aria-hidden="true"><path fill="currentColor" d="M3 20l18-8L3 4v6l12 2-12 2z"/></svg>';
  input.rows = 1;
  // Habilitado só quando a sessão estiver pronta, para nenhuma mensagem ser descartada.
  input.disabled = true;
  sendBtn.disabled = true;
  typing.appendChild(el('span', 'bubble', '• • •'));
  // Fica sempre por último na lista: novas mensagens entram antes dele.
  list.appendChild(typing);
  panel.setAttribute('role', 'dialog');
  list.setAttribute('aria-live', 'polite');

  titleBox.appendChild(titleEl);
  titleBox.appendChild(subtitleEl);
  header.appendChild(titleBox);
  header.appendChild(closeBtn);
  form.appendChild(input);
  form.appendChild(sendBtn);
  panel.appendChild(header);
  panel.appendChild(notice);
  panel.appendChild(list);
  panel.appendChild(form);
  panel.appendChild(footer);
  root.appendChild(style);
  root.appendChild(launcher);
  root.appendChild(panel);

  function applyStyle(color, position) {
    var safe = /^#[0-9a-fA-F]{6}$/.test(color) ? color : '#6941C6';
    var side = position === 'left' ? 'left' : 'right';
    style.textContent =
      ':host{all:initial}' +
      '*{box-sizing:border-box;font-family:Inter,system-ui,-apple-system,"Segoe UI",Roboto,sans-serif}' +
      '.launcher{position:fixed;' + side + ':20px;bottom:20px;width:60px;height:60px;border-radius:50%;border:0;background:' + safe + ';color:#fff;cursor:pointer;box-shadow:0 8px 24px rgba(16,24,40,.25);z-index:2147483000;display:grid;place-items:center;transition:transform .15s}' +
      '.launcher:hover{transform:scale(1.06)}' +
      '.panel{position:fixed;' + side + ':20px;bottom:92px;width:370px;max-width:calc(100vw - 32px);height:560px;max-height:calc(100vh - 120px);background:#fff;color:#101828;border-radius:16px;box-shadow:0 16px 48px rgba(16,24,40,.28);display:none;flex-direction:column;overflow:hidden;z-index:2147483000}' +
      '.panel.open{display:flex}' +
      '.header{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:14px 16px;background:' + safe + ';color:#fff}' +
      '.titles{display:flex;flex-direction:column;min-width:0}.title{font-size:15px}.subtitle{font-size:12px;opacity:.85}' +
      '.close{background:transparent;border:0;color:#fff;font-size:26px;line-height:1;cursor:pointer;padding:0 4px}' +
      '.notice{display:none;padding:8px 14px;font-size:12px;background:#fffaeb;color:#93370d;border-bottom:1px solid #fedf89}.notice.show{display:block}' +
      '.messages{flex:1;overflow-y:auto;padding:16px;display:flex;flex-direction:column;gap:10px;background:#f9fafb}' +
      '.msg{display:flex;max-width:85%}.msg.contact{align-self:flex-end}.msg.agent{align-self:flex-start}.msg.system{align-self:center;max-width:95%}' +
      '.bubble{padding:9px 13px;border-radius:14px;font-size:14px;line-height:1.45;white-space:pre-wrap;word-break:break-word}' +
      '.contact .bubble{background:' + safe + ';color:#fff;border-bottom-right-radius:4px}' +
      '.agent .bubble{background:#fff;border:1px solid #eaecf0;border-bottom-left-radius:4px}' +
      '.system .bubble{background:#f2f4f7;color:#475467;font-size:13px;text-align:center}' +
      '.agent.simulated .bubble{border:1px dashed #f79009;background:#fffaeb}' +
      '.badge{display:block;font-size:10px;font-weight:800;letter-spacing:.06em;color:#dc6803;margin-bottom:3px}.badge.human{color:#1570ef}' +
      '.typing{display:none}.typing.show{display:flex}.typing .bubble{color:#98a2b3;letter-spacing:2px}' +
      '.composer{display:flex;gap:8px;padding:10px 12px;border-top:1px solid #eaecf0;background:#fff}' +
      'textarea{flex:1;resize:none;border:1px solid #d0d5dd;border-radius:10px;padding:9px 12px;font-size:14px;max-height:110px;outline:none;color:#101828}' +
      'textarea:focus{border-color:' + safe + '}' +
      '.send{width:42px;border:0;border-radius:10px;background:' + safe + ';color:#fff;cursor:pointer;display:grid;place-items:center}.send:disabled{opacity:.5;cursor:default}' +
      '.error{color:#d92d20;font-size:12px;padding:0 14px 6px}' +
      '.footer{font-size:10px;color:#98a2b3;text-align:center;padding:0 0 8px;background:#fff}' +
      '@media (max-width:480px){.panel{' + side + ':8px;bottom:84px;width:calc(100vw - 16px);height:calc(100vh - 100px)}}';
  }

  function scrollDown() { list.scrollTop = list.scrollHeight; }

  function addMessage(m) {
    if (!m || state.messages[m.id]) return;
    state.messages[m.id] = true;
    var kind = m.senderType === 'Contact' ? 'contact' : m.senderType === 'System' ? 'system' : 'agent';
    var row = el('div', 'msg ' + kind + (m.isSimulated ? ' simulated' : ''));
    var bubble = el('div', 'bubble');
    if (m.isSimulated) bubble.appendChild(el('span', 'badge', t.simulatedBadge));
    if (m.senderType === 'Human') bubble.appendChild(el('span', 'badge human', t.attendant));
    bubble.appendChild(document.createTextNode(m.content));
    row.appendChild(bubble);
    row.setAttribute('data-at', m.createdAt);
    // Mensagens chegam pela resposta HTTP e pelo SignalR em ordens diferentes: posiciona pela data de criação.
    var next = typing;
    var rows = list.querySelectorAll('.msg[data-at]');
    for (var i = rows.length - 1; i >= 0; i--) {
      if (rows[i].getAttribute('data-at') > m.createdAt) next = rows[i];
      else break;
    }
    list.insertBefore(row, next);
    if (!state.lastAt || m.createdAt > state.lastAt) state.lastAt = m.createdAt;
    scrollDown();
  }

  function showError(text) {
    var old = panel.querySelector('.error');
    if (old) old.remove();
    if (text) panel.insertBefore(el('div', 'error', text), form);
  }

  // ---------- Fluxo ----------
  function startSession(forceNew) {
    var saved = forceNew ? null : loadSession();
    return request('POST', '/sessions', { token: token, sessionId: saved && saved.sessionId, visitorKey: saved && saved.visitorKey })
      .then(function (s) {
        var key = s.visitorKey || (saved && saved.visitorKey);
        state.session = { sessionId: s.sessionId, visitorKey: key };
        saveSession(state.session);
        (s.messages || []).forEach(addMessage);
        connectRealtime();
      });
  }

  function init() {
    if (state.ready) return Promise.resolve();
    return request('GET', '/config?token=' + encodeURIComponent(token)).then(function (cfg) {
      state.config = cfg;
      t = TEXT[cfg.language] || TEXT['pt-BR'];
      applyStyle(cfg.primaryColor, cfg.position);
      titleEl.textContent = cfg.title;
      subtitleEl.textContent = cfg.subtitle || cfg.agentName;
      input.placeholder = t.placeholder;
      input.maxLength = cfg.maxMessageChars || 2000;
      sendBtn.setAttribute('aria-label', t.send);
      closeBtn.setAttribute('aria-label', t.close);
      launcher.setAttribute('aria-label', t.open);
      footer.textContent = t.powered;
      notice.textContent = t.simulated;
      notice.classList.toggle('show', !!cfg.simulatedMode);
      return startSession(false).then(function () {
        state.ready = true;
        input.disabled = false;
        sendBtn.disabled = false;
      });
    });
  }

  function send(text, retried) {
    if (!state.session || state.sending) return;
    state.sending = true;
    sendBtn.disabled = true;
    typing.classList.add('show');
    showError(null);
    scrollDown();
    request('POST', '/messages', { token: token, sessionId: state.session.sessionId, visitorKey: state.session.visitorKey, content: text })
      .then(function (r) { addMessage(r.userMessage); if (r.reply) addMessage(r.reply); })
      .catch(function (err) {
        // Conversa encerrada pela equipe: começa outra e reenvia uma única vez.
        if (err.code === 'conversation_closed' && !retried) {
          state.sending = false;
          return startSession(true).then(function () { send(text, true); });
        }
        input.value = text;
        showError(err.status === 429 ? err.message : t.error);
      })
      .then(function () {
        state.sending = false;
        sendBtn.disabled = false;
        typing.classList.remove('show');
      });
  }

  // ---------- Tempo real (SignalR; se indisponível, consulta periódica) ----------
  function loadSignalR() {
    if (window.signalR) return Promise.resolve(window.signalR);
    return new Promise(function (resolve, reject) {
      var s = document.createElement('script');
      s.src = base + '/widget/signalr.min.js';
      s.async = true;
      s.onload = function () { window.signalR ? resolve(window.signalR) : reject(new Error('signalR')); };
      s.onerror = reject;
      document.head.appendChild(s);
    });
  }

  function startPolling() {
    if (state.poll) return;
    state.poll = setInterval(function () {
      if (!state.session || !state.open) return;
      request('POST', '/messages/list', { token: token, sessionId: state.session.sessionId, visitorKey: state.session.visitorKey, after: state.lastAt })
        .then(function (items) { items.forEach(addMessage); })
        .catch(function () { /* tenta no próximo ciclo */ });
    }, 5000);
  }

  function connectRealtime() {
    if (state.hub) {
      state.hub.invoke('Join', token, state.session.sessionId, state.session.visitorKey).catch(startPolling);
      return;
    }
    loadSignalR().then(function (signalR) {
      var hub = new signalR.HubConnectionBuilder()
        .withUrl(base + '/hubs/webchat', { withCredentials: false })
        .withAutomaticReconnect()
        .configureLogging(signalR.LogLevel.None)
        .build();
      hub.on('message', addMessage);
      hub.onreconnected(function () { hub.invoke('Join', token, state.session.sessionId, state.session.visitorKey); });
      state.hub = hub;
      return hub.start().then(function () {
        return hub.invoke('Join', token, state.session.sessionId, state.session.visitorKey);
      }).then(function (joined) { if (!joined) startPolling(); });
    }).catch(startPolling);
  }

  // ---------- Eventos ----------
  function toggle(open) {
    state.open = open;
    panel.classList.toggle('open', open);
    launcher.setAttribute('aria-expanded', String(open));
    if (open) {
      init().then(function () { input.focus(); scrollDown(); }).catch(function () {
        notice.textContent = t.unavailable;
        notice.classList.add('show');
        input.disabled = true;
        sendBtn.disabled = true;
      });
    }
  }

  launcher.addEventListener('click', function () { toggle(!state.open); });
  closeBtn.addEventListener('click', function () { toggle(false); launcher.focus(); });
  form.addEventListener('submit', function (e) {
    e.preventDefault();
    var text = input.value.trim();
    if (!text || !state.session || state.sending) return;
    input.value = '';
    send(text, false);
  });
  input.addEventListener('keydown', function (e) {
    if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) { e.preventDefault(); form.requestSubmit ? form.requestSubmit() : form.dispatchEvent(new Event('submit')); }
  });

  applyStyle('#6941C6', 'right');
  function mount() { document.body.appendChild(host); }
  document.body ? mount() : document.addEventListener('DOMContentLoaded', mount);
})();

using System;

namespace AIInfluencePrism.UI
{
    /// <summary>
    /// Embedded Single Page Application (HTML/CSS/JS) for Prism Settings WebUI.
    /// Served by PrismWebUiHost on http://127.0.0.1:43210/.
    /// </summary>
    public static class PrismWebUiHtml
    {
        public static string GetHtml()
        {
            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>Prism 1.0.0 — AIInfluence Bridge</title>
<style>
  :root {
    --bg-main: #0d1117;
    --bg-card: #161b22;
    --bg-subtle: #21262d;
    --border: #30363d;
    --accent: #10b981;
    --accent-hover: #059669;
    --accent-glow: rgba(16, 185, 129, 0.2);
    --gold: #d97706;
    --text-primary: #f0f6fc;
    --text-secondary: #8b949e;
    --text-muted: #6e7681;
    --danger: #ef4444;
    --radius: 8px;
    --font: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
  }
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    background-color: var(--bg-main);
    color: var(--text-primary);
    font-family: var(--font);
    font-size: 14px;
    line-height: 1.5;
    height: 100vh;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    user-select: none;
  }
  /* Header */
  header {
    background-color: var(--bg-card);
    border-bottom: 1px solid var(--border);
    padding: 12px 24px;
    display: flex;
    align-items: center;
    justify-content: space-between;
    flex-shrink: 0;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 12px;
  }
  .brand-icon {
    font-size: 24px;
    filter: drop-shadow(0 0 8px var(--accent));
  }
  .brand-title {
    font-size: 18px;
    font-weight: 700;
    letter-spacing: 0.5px;
    background: linear-gradient(135deg, #34d399, #10b981, #06b6d4);
    -webkit-background-clip: text;
    -webkit-text-fill-color: transparent;
  }
  .status-badge {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
    font-weight: 600;
    padding: 4px 10px;
    border-radius: 9999px;
    background: rgba(16, 185, 129, 0.15);
    color: #34d399;
    border: 1px solid rgba(16, 185, 129, 0.3);
  }
  .status-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: #10b981;
    box-shadow: 0 0 8px #10b981;
  }
  .header-actions {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .btn {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    padding: 7px 14px;
    font-size: 13px;
    font-weight: 600;
    border-radius: var(--radius);
    border: 1px solid var(--border);
    background-color: var(--bg-subtle);
    color: var(--text-primary);
    cursor: pointer;
    transition: all 0.15s ease;
  }
  .btn:hover {
    background-color: #30363d;
    border-color: #8b949e;
  }
  .btn-primary {
    background-color: var(--accent);
    color: #0d1117;
    border-color: var(--accent);
  }
  .btn-primary:hover {
    background-color: var(--accent-hover);
    border-color: var(--accent-hover);
    box-shadow: 0 0 12px var(--accent-glow);
  }
  .btn-close {
    background-color: transparent;
    border-color: transparent;
    color: var(--text-secondary);
    font-size: 16px;
    padding: 7px 10px;
  }
  .btn-close:hover {
    background-color: rgba(239, 68, 68, 0.15);
    color: var(--danger);
    border-color: rgba(239, 68, 68, 0.3);
  }
  /* Nav Tabs */
  nav {
    display: flex;
    background-color: var(--bg-card);
    border-bottom: 1px solid var(--border);
    padding: 0 24px;
    flex-shrink: 0;
  }
  .tab-btn {
    padding: 10px 18px;
    font-size: 13px;
    font-weight: 600;
    color: var(--text-secondary);
    border-bottom: 2px solid transparent;
    cursor: pointer;
    background: none;
    border-top: none;
    border-left: none;
    border-right: none;
    transition: all 0.15s ease;
  }
  .tab-btn:hover {
    color: var(--text-primary);
  }
  .tab-btn.active {
    color: var(--accent);
    border-bottom-color: var(--accent);
  }
  /* Main Container */
  main {
    flex: 1;
    overflow-y: auto;
    padding: 24px;
  }
  .tab-pane {
    display: none;
    max-width: 960px;
    margin: 0 auto;
  }
  .tab-pane.active {
    display: block;
    animation: fadeIn 0.15s ease;
  }
  @keyframes fadeIn {
    from { opacity: 0; transform: translateY(4px); }
    to { opacity: 1; transform: translateY(0); }
  }
  /* Cards & Sections */
  .card {
    background-color: var(--bg-card);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 20px;
    margin-bottom: 20px;
  }
  .card-title {
    font-size: 16px;
    font-weight: 600;
    margin-bottom: 14px;
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  .form-group {
    margin-bottom: 16px;
  }
  .form-label {
    display: block;
    font-size: 12px;
    font-weight: 600;
    color: var(--text-secondary);
    margin-bottom: 6px;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }
  .form-input, .form-select, .form-textarea {
    width: 100%;
    padding: 8px 12px;
    font-size: 14px;
    color: var(--text-primary);
    background-color: var(--bg-subtle);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    outline: none;
    transition: border-color 0.15s ease;
    font-family: inherit;
  }
  .form-input:focus, .form-select:focus, .form-textarea:focus {
    border-color: var(--accent);
    box-shadow: 0 0 0 2px var(--accent-glow);
  }
  .form-textarea {
    resize: vertical;
    min-height: 80px;
    user-select: text;
  }
  /* Provider Grid */
  .provider-grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(130px, 1fr));
    gap: 10px;
    margin-bottom: 16px;
  }
  .provider-card {
    background-color: var(--bg-subtle);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 12px;
    text-align: center;
    cursor: pointer;
    transition: all 0.15s ease;
  }
  .provider-card:hover {
    border-color: #8b949e;
  }
  .provider-card.selected {
    background-color: rgba(16, 185, 129, 0.1);
    border-color: var(--accent);
    box-shadow: 0 0 8px var(--accent-glow);
  }
  .provider-name {
    font-weight: 600;
    font-size: 13px;
    margin-top: 4px;
  }
  /* Scope Cards */
  .scope-item {
    background-color: var(--bg-subtle);
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 14px;
    margin-bottom: 12px;
  }
  .scope-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    margin-bottom: 8px;
  }
  .scope-title {
    font-weight: 600;
    font-size: 14px;
  }
  .toggle {
    position: relative;
    display: inline-block;
    width: 38px;
    height: 20px;
  }
  .toggle input { opacity: 0; width: 0; height: 0; }
  .slider {
    position: absolute; cursor: pointer; top: 0; left: 0; right: 0; bottom: 0;
    background-color: #30363d;
    transition: .2s;
    border-radius: 20px;
  }
  .slider:before {
    position: absolute; content: """"; height: 14px; width: 14px; left: 3px; bottom: 3px;
    background-color: #f0f6fc;
    transition: .2s;
    border-radius: 50%;
  }
  input:checked + .slider { background-color: var(--accent); }
  input:checked + .slider:before { transform: translateX(18px); background-color: #0d1117; }
  /* Toast Notification */
  #toast {
    position: fixed;
    bottom: 24px;
    right: 24px;
    padding: 10px 18px;
    background-color: #10b981;
    color: #0d1117;
    font-weight: 600;
    font-size: 13px;
    border-radius: var(--radius);
    box-shadow: 0 4px 16px rgba(0,0,0,0.4);
    opacity: 0;
    transform: translateY(10px);
    transition: all 0.25s ease;
    pointer-events: none;
    z-index: 1000;
  }
  #toast.show {
    opacity: 1;
    transform: translateY(0);
  }
  /* Test Output */
  .test-output {
    background-color: #090c10;
    border: 1px solid var(--border);
    border-radius: var(--radius);
    padding: 14px;
    margin-top: 14px;
    font-family: Consolas, Monaco, monospace;
    font-size: 13px;
    white-space: pre-wrap;
    user-select: text;
    max-height: 250px;
    overflow-y: auto;
  }
</style>
</head>
<body>

<header>
  <div class=""brand"">
    <span class=""brand-icon"">⚔️</span>
    <span class=""brand-title"">PRISM 1.0.0</span>
    <span class=""status-badge""><span class=""status-dot""></span> Bridge Active (Port 4315)</span>
  </div>
  <div class=""header-actions"">
    <button class=""btn"" onclick=""openExternalBrowser()"">🌐 Open in Browser</button>
    <button class=""btn btn-primary"" onclick=""saveConfig()"">💾 Save & Apply</button>
    <button class=""btn btn-close"" onclick=""closeWindow()"">✕</button>
  </div>
</header>

<nav>
  <button class=""tab-btn active"" onclick=""switchTab('providers')"">⚡ Providers & Keys</button>
  <button class=""tab-btn"" onclick=""switchTab('scopes')"">🎯 Scopes (8)</button>
  <button class=""tab-btn"" onclick=""switchTab('testing')"">🧪 Live Chat Tester</button>
  <button class=""tab-btn"" onclick=""switchTab('diagnostics')"">📊 Diagnostics</button>
</nav>

<main>
  <!-- TAB 1: PROVIDERS -->
  <div id=""tab-providers"" class=""tab-pane active"">
    <div class=""card"">
      <div class=""card-title"">
        <span>Master Bridge Control</span>
        <label class=""toggle"">
          <input type=""checkbox"" id=""master-enabled"" checked>
          <span class=""slider""></span>
        </label>
      </div>
      <p style=""color: var(--text-secondary); font-size: 13px;"">
        When enabled, Prism intercepts all AIInfluence dialogue, troop, and quest requests and routes them directly to your chosen LLM provider.
      </p>
    </div>

    <div class=""card"">
      <div class=""card-title"">Select LLM Provider</div>
      <div class=""provider-grid"" id=""provider-grid"">
        <!-- Rendered by JS -->
      </div>

      <div class=""form-group"">
        <label class=""form-label"">Endpoint Root URL</label>
        <div style=""display: flex; gap: 8px;"">
          <input type=""text"" id=""endpoint-url"" class=""form-input"" placeholder=""https://generativelanguage.googleapis.com"">
          <button class=""btn"" onclick=""applyDefaultEndpoint()"">Default</button>
        </div>
      </div>

      <div class=""form-group"">
        <label class=""form-label"">API Key / Token</label>
        <input type=""password"" id=""api-key"" class=""form-input"" placeholder=""Enter API key or leave blank for local providers"">
      </div>

      <div style=""display: grid; grid-template-columns: 2fr 1fr 1fr; gap: 12px;"">
        <div class=""form-group"">
          <label class=""form-label"">Model Name (Free Text / Custom)</label>
          <input type=""text"" id=""model-name"" class=""form-input"" list=""prism-provider-models"" placeholder=""Type custom model or choose suggestion (e.g. gemini-2.0-flash, gpt-4o, deepseek-chat)"">
        </div>
        <div class=""form-group"">
          <label class=""form-label"">Temperature (<span id=""temp-val"">0.7</span>)</label>
          <input type=""range"" id=""temperature"" min=""0"" max=""1.5"" step=""0.05"" value=""0.7"" class=""form-input"" style=""padding: 0;"" oninput=""document.getElementById('temp-val').innerText = this.value"">
        </div>
        <div class=""form-group"">
          <label class=""form-label"">Timeout (sec)</label>
          <input type=""number"" id=""timeout-seconds"" class=""form-input"" value=""60"" min=""5"" max=""300"">
        </div>
      </div>
    </div>
  </div>

  <!-- TAB 2: SCOPES -->
  <div id=""tab-scopes"" class=""tab-pane"">
    <div class=""card"">
      <div class=""card-title"">Scope Routing & Prompts</div>
      <p style=""color: var(--text-secondary); font-size: 13px; margin-bottom: 16px;"">
        Prism separates AIInfluence calls into 8 isolated scopes. You can independently enable scopes and customize model overrides.
      </p>
      <div id=""scope-list"">
        <!-- Rendered by JS -->
      </div>
    </div>
  </div>

  <!-- TAB 3: TESTING -->
  <div id=""tab-testing"" class=""tab-pane"">
    <div class=""card"">
      <div class=""card-title"">Live LLM Completion Playground</div>
      <div class=""form-group"">
        <label class=""form-label"">Target Scope</label>
        <select id=""test-scope"" class=""form-select"">
          <option value=""DialogueHeroes"">Dialogue: Heroes (Nobles & Lords)</option>
          <option value=""DialogueTownsfolk"">Dialogue: Townsfolk & Commoners</option>
          <option value=""NpcLetters"">NPC Letters</option>
          <option value=""NpcInitiative"">NPC Initiative</option>
          <option value=""CharacterDevelopment"">Character Development</option>
          <option value=""BattleTactics"">Battle Tactics</option>
          <option value=""Diplomacy"">Diplomacy & Events</option>
          <option value=""MemoryBook"">Memory Book</option>
        </select>
      </div>
      <div class=""form-group"">
        <label class=""form-label"">Test Prompt / Character Message</label>
        <textarea id=""test-prompt"" class=""form-textarea"">Приветствую, путник. Что слышно в пути?</textarea>
      </div>
      <button class=""btn btn-primary"" id=""btn-test"" onclick=""runTestPrompt()"">🚀 Send Test Request</button>
      <div id=""test-result"" class=""test-output"" style=""display: none;""></div>
    </div>
  </div>

  <!-- TAB 4: DIAGNOSTICS -->
  <div id=""tab-diagnostics"" class=""tab-pane"">
    <div class=""card"">
      <div class=""card-title"">System & Hook Diagnostics</div>
      <div style=""display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-bottom: 16px;"">
        <div style=""background: var(--bg-subtle); padding: 12px; border-radius: var(--radius);"">
          <div style=""color: var(--text-secondary); font-size: 11px;"">BCL REFLECTION REPLACEMENTS</div>
          <div style=""font-size: 18px; font-weight: 700; color: var(--accent); margin-top: 4px;"">7 / 7 Fields Replaced</div>
        </div>
        <div style=""background: var(--bg-subtle); padding: 12px; border-radius: var(--radius);"">
          <div style=""color: var(--text-secondary); font-size: 11px;"">PLAYER2 TARGET PORT</div>
          <div style=""font-size: 18px; font-weight: 700; color: #60a5fa; margin-top: 4px;"">4315 (Loopback)</div>
        </div>
      </div>
      <p style=""color: var(--text-secondary); font-size: 13px;"">
        Prism replaces static HttpClient instances in AIInfluence assembly with a zero-timeout PrismHttpMessageHandler. All requests bypass Class681 hardcoded 10-second timers and route directly to the active LLM.
      </p>
    </div>
  </div>
</main>

<datalist id=""prism-provider-models"">
  <option value=""gemini-3.8-flash-high"">Gemini 3.8 Flash High (Default)</option>
  <option value=""gemini-3.8-flash-low"">Gemini 3.8 Flash Low</option>
  <option value=""gemini-2.0-flash"">Gemini 2.0 Flash</option>
  <option value=""gemini-2.5-flash"">Gemini 2.5 Flash</option>
  <option value=""gemini-2.5-pro"">Gemini 2.5 Pro</option>
  <option value=""gpt-4o"">OpenAI GPT-4o</option>
  <option value=""gpt-4o-mini"">OpenAI GPT-4o Mini</option>
  <option value=""gpt-4-turbo"">OpenAI GPT-4 Turbo</option>
  <option value=""o3-mini"">OpenAI o3 Mini</option>
  <option value=""deepseek-chat"">DeepSeek Chat (V3)</option>
  <option value=""deepseek-reasoner"">DeepSeek Reasoner (R1)</option>
  <option value=""claude-3-5-sonnet-latest"">Claude 3.5 Sonnet</option>
  <option value=""claude-3-5-haiku-latest"">Claude 3.5 Haiku</option>
  <option value=""llama3.3"">Ollama Llama 3.3</option>
  <option value=""llama3.2"">Ollama Llama 3.2</option>
  <option value=""llama3"">Ollama Llama 3</option>
  <option value=""qwen2.5-coder"">Qwen 2.5 Coder</option>
  <option value=""mistral-large-latest"">Mistral Large</option>
</datalist>

<datalist id=""prism-scope-models"">
  <option value=""gemini-3.8-flash-low"">Gemini 3.8 Flash Low (Fast Dialogue)</option>
  <option value=""gemini-3.8-flash-high"">Gemini 3.8 Flash High (Rich Roleplay)</option>
  <option value=""gemini-2.0-flash"">Gemini 2.0 Flash</option>
  <option value=""gpt-4o-mini"">GPT-4o Mini</option>
  <option value=""gpt-4o"">GPT-4o</option>
  <option value=""deepseek-chat"">DeepSeek Chat</option>
  <option value=""deepseek-reasoner"">DeepSeek Reasoner</option>
  <option value=""claude-3-5-sonnet-latest"">Claude 3.5 Sonnet</option>
  <option value=""claude-3-5-haiku-latest"">Claude 3.5 Haiku</option>
  <option value=""llama3.2"">Llama 3.2 (Local)</option>
</datalist>

<div id=""toast"">Settings saved & applied!</div>

<script>
const PROVIDERS = [
  { id: 'Gemini', name: 'Google Gemini', defaultUrl: 'https://generativelanguage.googleapis.com', defaultModel: 'gemini-2.0-flash' },
  { id: 'Antigravity', name: 'Antigravity CLI', defaultUrl: '', defaultModel: 'gemini-3.8-flash-high' },
  { id: 'OpenAiCompat', name: 'OpenAI / ChatGPT', defaultUrl: 'https://api.openai.com', defaultModel: 'gpt-4o-mini' },
  { id: 'Player2', name: 'Player2 Local', defaultUrl: 'http://127.0.0.1:4315', defaultModel: 'gemini3-flash' },
  { id: 'KoboldCpp', name: 'KoboldCpp', defaultUrl: 'http://127.0.0.1:5001', defaultModel: '' },
  { id: 'Ollama', name: 'Ollama Local', defaultUrl: 'http://127.0.0.1:11434', defaultModel: 'llama3.2' },
  { id: 'DeepSeek', name: 'DeepSeek API', defaultUrl: 'https://api.deepseek.com', defaultModel: 'deepseek-chat' },
  { id: 'Anthropic', name: 'Claude Anthropic', defaultUrl: 'https://api.anthropic.com', defaultModel: 'claude-3-5-sonnet-latest' },
  { id: 'Custom', name: 'Custom OpenAI', defaultUrl: 'http://localhost:8000', defaultModel: '' }
];

const SCOPES = [
  { id: 'DialogueHeroes', name: 'Dialogue: Heroes (Lords, Ladies, Companions)' },
  { id: 'DialogueTownsfolk', name: 'Dialogue: Townsfolk, Troops & Commoners' },
  { id: 'NpcLetters', name: 'NPC Letters & Correspondence' },
  { id: 'NpcInitiative', name: 'NPC Initiative & Autonomous Actions' },
  { id: 'CharacterDevelopment', name: 'Character Development & Traits' },
  { id: 'BattleTactics', name: 'Battle Tactics & Combat Banter' },
  { id: 'Diplomacy', name: 'Diplomacy, Treaties & Kingdom Events' },
  { id: 'MemoryBook', name: 'Memory Book & Character Lore' }
];

let selectedProvider = 'Gemini';
let configData = null;

function init() {
  renderProviderGrid();
  renderScopeList();
  loadConfig();
}

function switchTab(name) {
  document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.tab-pane').forEach(p => p.classList.remove('active'));
  event.target.classList.add('active');
  document.getElementById('tab-' + name).classList.add('active');
}

function renderProviderGrid() {
  const grid = document.getElementById('provider-grid');
  grid.innerHTML = PROVIDERS.map(p => `
    <div class=""provider-card ${p.id === selectedProvider ? 'selected' : ''}"" onclick=""selectProvider('${p.id}')"">
      <div class=""provider-name"">${p.name}</div>
    </div>
  `).join('');
}

function selectProvider(id) {
  selectedProvider = id;
  renderProviderGrid();
  const p = PROVIDERS.find(x => x.id === id);
  if (p && !document.getElementById('endpoint-url').value) {
    document.getElementById('endpoint-url').value = p.defaultUrl;
  }
}

function applyDefaultEndpoint() {
  const p = PROVIDERS.find(x => x.id === selectedProvider);
  if (p) {
    document.getElementById('endpoint-url').value = p.defaultUrl;
    if (p.defaultModel && !document.getElementById('model-name').value) {
      document.getElementById('model-name').value = p.defaultModel;
    }
  }
}

function renderScopeList() {
  const container = document.getElementById('scope-list');
  container.innerHTML = SCOPES.map(s => `
    <div class=""scope-item"">
      <div class=""scope-header"">
        <span class=""scope-title"">${s.name}</span>
        <label class=""toggle"">
          <input type=""checkbox"" id=""scope-enable-${s.id}"" checked>
          <span class=""slider""></span>
        </label>
      </div>
      <div style=""display: flex; gap: 8px; margin-top: 6px;"">
        <input type=""text"" id=""scope-model-${s.id}"" class=""form-input"" list=""prism-scope-models"" placeholder=""Override model (type any custom model or select suggestion)"">
      </div>
    </div>
  `).join('');
}

async function loadConfig() {
  try {
    const res = await fetch('/api/config');
    if (!res.ok) return;
    configData = await res.json();
    document.getElementById('master-enabled').checked = configData.masterEnabled !== false;
    document.getElementById('endpoint-url').value = configData.rootUrl || '';
    document.getElementById('api-key').value = configData.apiKey || '';
    document.getElementById('model-name').value = configData.model || '';
    document.getElementById('temperature').value = configData.temperature || 0.7;
    document.getElementById('temp-val').innerText = configData.temperature || 0.7;
    document.getElementById('timeout-seconds').value = configData.timeoutSeconds || 60;
    if (configData.provider) {
      selectProvider(configData.provider);
    }
    if (configData.scopes) {
      for (const [id, s] of Object.entries(configData.scopes)) {
        const chk = document.getElementById('scope-enable-' + id);
        const mdl = document.getElementById('scope-model-' + id);
        if (chk && s.enabled !== undefined) chk.checked = s.enabled;
        if (mdl && s.model) mdl.value = s.model;
      }
    }
  } catch (e) {
    console.error('Failed to load config:', e);
  }
}

async function saveConfig() {
  const payload = {
    masterEnabled: document.getElementById('master-enabled').checked,
    provider: selectedProvider,
    rootUrl: document.getElementById('endpoint-url').value,
    apiKey: document.getElementById('api-key').value,
    model: document.getElementById('model-name').value,
    temperature: parseFloat(document.getElementById('temperature').value),
    timeoutSeconds: parseInt(document.getElementById('timeout-seconds').value) || 60,
    scopes: {}
  };
  SCOPES.forEach(s => {
    const chk = document.getElementById('scope-enable-' + s.id);
    const mdl = document.getElementById('scope-model-' + s.id);
    payload.scopes[s.id] = {
      enabled: chk ? chk.checked : true,
      model: mdl ? mdl.value : ''
    };
  });

  try {
    const res = await fetch('/api/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json; charset=utf-8' },
      body: JSON.stringify(payload)
    });
    if (res.ok) {
      showToast('Settings saved & applied!');
    }
  } catch (e) {
    showToast('Error saving settings: ' + e.message);
  }
}

async function runTestPrompt() {
  const scope = document.getElementById('test-scope').value;
  const prompt = document.getElementById('test-prompt').value;
  const btn = document.getElementById('btn-test');
  const out = document.getElementById('test-result');

  btn.disabled = true;
  btn.innerText = '⏳ Waiting for LLM...';
  out.style.display = 'block';
  out.innerText = 'Sending request to Pipeline...';

  try {
    const res = await fetch('/api/test', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json; charset=utf-8' },
      body: JSON.stringify({ scope, prompt })
    });
    const data = await res.json();
    if (data.success) {
      out.innerText = `[${data.elapsedMs}ms latency]\n\n${data.response}`;
    } else {
      out.innerText = `[Error]: ${data.error || 'Failed to generate response'}`;
    }
  } catch (e) {
    out.innerText = `[Network Error]: ${e.message}`;
  } finally {
    btn.disabled = false;
    btn.innerText = '🚀 Send Test Request';
  }
}

function showToast(msg) {
  const t = document.getElementById('toast');
  t.innerText = msg;
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 2500);
}

function openExternalBrowser() {
  fetch('/api/open-external', { method: 'POST' });
}

function closeWindow() {
  fetch('/api/close', { method: 'POST' });
}

window.onload = init;
</script>
</body>
</html>";
        }
    }
}

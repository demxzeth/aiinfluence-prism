using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AIInfluencePrism.Infra;
using AIInfluencePrism.Logging;
using AIInfluencePrism.Profiles;
using AIInfluencePrism.Routing;
using AIInfluencePrism.Transport;

namespace AIInfluencePrism.UI
{
    /// <summary>
    /// In-game WebUI Host for Prism 1.0.0.
    /// Hosts a lightweight embedded HTTP server on loopback (http://127.0.0.1:43210/)
    /// and manages a borderless WinForms + WebView2 overlay window attached to Bannerlord.
    /// Includes seamless fallback to the user's default browser if WebView2 runtime is absent.
    /// </summary>
    public static class PrismWebUiHost
    {
        public const string ServerUrl = "http://127.0.0.1:43210/";
        private const int GWLP_HWNDPARENT = -8;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static bool _serverRunning;
        private static bool _webViewSupported = true;

        private static Form _overlayForm;
        private static WebView2 _webViewControl;
        private static Thread _staThread;
        private static readonly object _syncLock = new object();
        private static IntPtr _gameHwnd = IntPtr.Zero;

        public static bool IsOpen => _overlayForm != null && _overlayForm.Visible;

        /// <summary>
        /// Starts the embedded HTTP server on loopback port 43210.
        /// </summary>
        public static void Start()
        {
            lock (_syncLock)
            {
                if (_serverRunning)
                {
                    return;
                }

                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add(ServerUrl);
                    _listener.Start();
                    _cts = new CancellationTokenSource();
                    _serverRunning = true;

                    Task.Run(() => ServerLoopAsync(_listener, _cts.Token));
                    LogSink.Always($"[PrismWebUiHost] HTTP WebUI server listening on {ServerUrl}");
                }
                catch (Exception ex)
                {
                    LogSink.Error("[PrismWebUiHost] Failed to start HTTP WebUI server: " + ex.Message, ex);
                }
            }
        }

        /// <summary>
        /// Stops the HTTP server and cleans up the overlay form.
        /// </summary>
        public static void Stop()
        {
            lock (_syncLock)
            {
                try
                {
                    _cts?.Cancel();
                    if (_listener != null && _listener.IsListening)
                    {
                        _listener.Stop();
                        _listener.Close();
                    }
                    _listener = null;
                    _serverRunning = false;
                }
                catch { }

                Close();
            }
        }

        /// <summary>
        /// Opens the Prism Settings UI.
        /// Tries to show the borderless WebView2 overlay attached to Bannerlord.
        /// If WebView2 is unavailable, launches the user's default web browser.
        /// </summary>
        public static void Open()
        {
            if (!_serverRunning)
            {
                Start();
            }

            if (!_webViewSupported)
            {
                LaunchExternalBrowser();
                return;
            }

            lock (_syncLock)
            {
                try
                {
                    if (_overlayForm != null && !_overlayForm.IsDisposed)
                    {
                        _overlayForm.BeginInvoke(new Action(() =>
                        {
                            UpdateWindowPosition(_overlayForm);
                            _overlayForm.Show();
                            _overlayForm.BringToFront();
                            _overlayForm.Focus();
                        }));
                        return;
                    }

                    _staThread = new Thread(RunFormSTA)
                    {
                        IsBackground = true
                    };
                    _staThread.SetApartmentState(ApartmentState.STA);
                    _staThread.Start();
                }
                catch (Exception ex)
                {
                    LogSink.Warn("[PrismWebUiHost] Failed to open WebView2 overlay; falling back to browser: " + ex.Message);
                    _webViewSupported = false;
                    LaunchExternalBrowser();
                }
            }
        }

        /// <summary>
        /// Closes and hides the overlay form, restoring focus to Bannerlord.
        /// </summary>
        public static void Close()
        {
            try
            {
                if (_overlayForm != null && !_overlayForm.IsDisposed)
                {
                    if (_overlayForm.InvokeRequired)
                    {
                        _overlayForm.BeginInvoke(new Action(() =>
                        {
                            _overlayForm.Hide();
                            RestoreGameFocus();
                        }));
                    }
                    else
                    {
                        _overlayForm.Hide();
                        RestoreGameFocus();
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[PrismWebUiHost] Error closing overlay form: " + ex.Message);
            }
        }

        public static void Tick()
        {
            if (_overlayForm != null && _overlayForm.Visible && !_overlayForm.IsDisposed)
            {
                try
                {
                    _overlayForm.BeginInvoke(new Action(() => UpdateWindowPosition(_overlayForm)));
                }
                catch { }
            }
        }

        private static void RestoreGameFocus()
        {
            try
            {
                if (_gameHwnd != IntPtr.Zero)
                {
                    SetForegroundWindow(_gameHwnd);
                    SetFocus(_gameHwnd);
                }
                else
                {
                    IntPtr hwnd = Process.GetCurrentProcess().MainWindowHandle;
                    if (hwnd != IntPtr.Zero)
                    {
                        SetForegroundWindow(hwnd);
                        SetFocus(hwnd);
                    }
                }
            }
            catch { }
        }

        private static void LaunchExternalBrowser()
        {
            try
            {
                Process.Start(new ProcessStartInfo(ServerUrl) { UseShellExecute = true });
                LogSink.Always($"[PrismWebUiHost] Opened WebUI in external browser: {ServerUrl}");
            }
            catch (Exception ex)
            {
                LogSink.Error("[PrismWebUiHost] Failed to launch external browser: " + ex.Message, ex);
            }
        }

        private static void UpdateWindowPosition(Form form)
        {
            try
            {
                if (_gameHwnd == IntPtr.Zero)
                {
                    _gameHwnd = Process.GetCurrentProcess().MainWindowHandle;
                }

                if (_gameHwnd != IntPtr.Zero && GetClientRect(_gameHwnd, out RECT rect))
                {
                    POINT pt = new POINT { X = 0, Y = 0 };
                    ClientToScreen(_gameHwnd, ref pt);

                    int clientW = rect.Right - rect.Left;
                    int clientH = rect.Bottom - rect.Top;

                    int w = Math.Max(900, (int)(clientW * 0.90));
                    int h = Math.Max(600, (int)(clientH * 0.90));

                    int x = pt.X + (clientW - w) / 2;
                    int y = pt.Y + (clientH - h) / 2;

                    form.Bounds = new Rectangle(x, y, w, h);
                }
            }
            catch { }
        }

        private static void RunFormSTA()
        {
            try
            {
                _gameHwnd = Process.GetCurrentProcess().MainWindowHandle;

                _overlayForm = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    ShowInTaskbar = false,
                    BackColor = Color.FromArgb(13, 17, 23),
                    KeyPreview = true
                };

                _overlayForm.KeyDown += (s, e) =>
                {
                    if (e.KeyCode == Keys.Escape)
                    {
                        Close();
                        e.Handled = true;
                    }
                };

                UpdateWindowPosition(_overlayForm);

                if (_gameHwnd != IntPtr.Zero)
                {
                    try
                    {
                        SetWindowLongPtr(_overlayForm.Handle, GWLP_HWNDPARENT, _gameHwnd);
                    }
                    catch { }
                }

                _webViewControl = new WebView2
                {
                    Dock = DockStyle.Fill
                };

                _overlayForm.Controls.Add(_webViewControl);

                _overlayForm.Load += async (s, e) =>
                {
                    try
                    {
                        string userDataDir = Path.Combine(ModuleLocator.Root, "Config", "WebView2UserData");
                        if (!Directory.Exists(userDataDir))
                        {
                            Directory.CreateDirectory(userDataDir);
                        }

                        CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
                        await _webViewControl.EnsureCoreWebView2Async(env);

                        _webViewControl.CoreWebView2.Settings.AreDevToolsEnabled = true;
                        _webViewControl.CoreWebView2.Settings.IsStatusBarEnabled = false;
                        _webViewControl.Source = new Uri(ServerUrl);
                    }
                    catch (Exception ex)
                    {
                        LogSink.Warn("[PrismWebUiHost] WebView2 init failed: " + ex.Message);
                        _webViewSupported = false;
                        Close();
                        LaunchExternalBrowser();
                    }
                };

                Application.Run(_overlayForm);
            }
            catch (Exception ex)
            {
                LogSink.Error("[PrismWebUiHost] Form STA thread error: " + ex.Message, ex);
                _webViewSupported = false;
                LaunchExternalBrowser();
            }
        }

        // ─── HTTP Server Loop ────────────────────────────────────────────────────────

        private static async Task ServerLoopAsync(HttpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && listener.IsListening)
            {
                try
                {
                    HttpListenerContext context = await listener.GetContextAsync().ConfigureAwait(false);
                    _ = Task.Run(() => HandleRequestAsync(context));
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (ct.IsCancellationRequested) break;
                    LogSink.Warn("[PrismWebUiHost] Listener loop error: " + ex.Message);
                }
            }
        }

        private static async Task HandleRequestAsync(HttpListenerContext ctx)
        {
            try
            {
                HttpListenerRequest req = ctx.Request;
                HttpListenerResponse resp = ctx.Response;

                // Add CORS headers for convenient local testing
                resp.Headers.Add("Access-Control-Allow-Origin", "*");
                resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
                resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

                if (string.Equals(req.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    resp.StatusCode = (int)HttpStatusCode.NoContent;
                    resp.Close();
                    return;
                }

                string path = req.Url.AbsolutePath.ToLowerInvariant();

                if (path == "/" || path == "/index.html")
                {
                    byte[] htmlBytes = Encoding.UTF8.GetBytes(GetHtmlContent());
                    resp.ContentType = "text/html; charset=utf-8";
                    resp.ContentLength64 = htmlBytes.Length;
                    await resp.OutputStream.WriteAsync(htmlBytes, 0, htmlBytes.Length).ConfigureAwait(false);
                    resp.Close();
                    return;
                }

                if (path == "/api/config")
                {
                    if (string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        Profile cur = ProfileBook.Current;
                        string curProvLabel = Profile.Label(cur.Provider);
                        if (string.IsNullOrWhiteSpace(curProvLabel)) curProvLabel = "Antigravity";

                        var obj = new JObject
                        {
                            ["activeProfile"] = string.IsNullOrWhiteSpace(cur.Name) ? "Default" : cur.Name,
                            ["profiles"] = new JArray("Default", "Roleplay-High", "Fast-Combat"),
                            ["general"] = new JObject
                            {
                                ["defaultProvider"] = curProvLabel,
                                ["logLevel"] = cur.LogEnabled ? "Info" : "Off"
                            },
                            ["providers"] = new JObject(),
                            ["scopes"] = new JObject(),

                            // Legacy fields for backward compatibility
                            ["masterEnabled"] = cur.MasterEnabled,
                            ["provider"] = cur.Provider.ToString(),
                            ["rootUrl"] = cur.RootUrl ?? string.Empty,
                            ["apiKey"] = cur.ApiKey ?? string.Empty,
                            ["model"] = cur.Model ?? string.Empty,
                            ["temperature"] = cur.Temperature,
                            ["maxTokens"] = cur.MaxTokens,
                            ["timeoutSeconds"] = cur.TimeoutSeconds
                        };

                        var provsObj = (JObject)obj["providers"];
                        string[] allProviders = new string[] { "Gemini", "Antigravity", "OpenAI", "KoboldCpp", "DeepSeek", "Ollama", "OpenRouter" };
                        foreach (string prov in allProviders)
                        {
                            string apiKey = string.Empty;
                            string model = string.Empty;
                            string endpoint = string.Empty;

                            if (cur.Providers != null && cur.Providers.TryGetValue(prov, out ProviderSettings ps) && ps != null)
                            {
                                apiKey = ps.ApiKey ?? string.Empty;
                                model = ps.Model ?? string.Empty;
                                endpoint = ps.RootUrl ?? string.Empty;
                            }
                            else if (string.Equals(prov, curProvLabel, StringComparison.OrdinalIgnoreCase))
                            {
                                apiKey = cur.ApiKey ?? string.Empty;
                                model = cur.Model ?? string.Empty;
                                endpoint = cur.RootUrl ?? string.Empty;
                            }
                            else if (Profile.TryParseProvider(prov, out ProviderKind pk))
                            {
                                var def = ProviderSettings.CreateDefault(pk);
                                model = def.Model ?? string.Empty;
                                endpoint = pk == ProviderKind.Custom ? string.Empty : (def.RootUrl ?? string.Empty);
                            }

                            if (string.Equals(prov, "Antigravity", StringComparison.OrdinalIgnoreCase))
                            {
                                if (string.IsNullOrEmpty(endpoint)) endpoint = cur.AntigravityCliPath ?? "agy.exe";
                                if (string.IsNullOrEmpty(model)) model = cur.AntigravityModel ?? "gemini-3.8-flash-low";
                            }

                            provsObj[prov] = new JObject
                            {
                                ["apiKey"] = apiKey,
                                ["model"] = model,
                                ["endpoint"] = endpoint
                            };
                        }

                        var scopesObj = (JObject)obj["scopes"];
                        var scopePairs = new (string Key, ScopeId Id, double DefTemp, int DefTokens)[]
                        {
                            ("DialogueHeroes", ScopeId.DialogueHeroes, 0.7, 0),
                            ("DialogueTownsfolk", ScopeId.DialogueTownsfolk, 0.6, 0),
                            ("NpcLetters", ScopeId.NpcLetters, 0.7, 0),
                            ("Initiative", ScopeId.NpcInitiative, 0.5, 0),
                            ("CharacterDevelopment", ScopeId.CharacterDevelopment, 0.4, 0),
                            ("Tactics", ScopeId.BattleTactics, 0.3, 0),
                            ("Diplomacy", ScopeId.Diplomacy, 0.5, 0),
                            ("MemoryBook", ScopeId.MemoryBook, 0.4, 0)
                        };

                        foreach (var pair in scopePairs)
                        {
                            var s = cur.Scope(pair.Id);
                            string scProv = Profile.Label(s.Provider);
                            if (string.IsNullOrWhiteSpace(scProv) || s.Provider == ProviderKind.Player2)
                            {
                                scProv = curProvLabel;
                            }
                            string scModel = !string.IsNullOrWhiteSpace(s.Model) ? s.Model : (cur.Model ?? string.Empty);

                            scopesObj[pair.Key] = new JObject
                            {
                                ["enabled"] = s.Enabled,
                                ["provider"] = scProv,
                                ["model"] = scModel,
                                ["temperature"] = s.Temperature > 0 ? s.Temperature : pair.DefTemp,
                                ["maxTokens"] = s.MaxTokens > 0 ? s.MaxTokens : 0,
                                ["systemPrompt"] = s.SystemPrompt ?? string.Empty
                            };
                        }

                        obj["sessionDialogues"] = DiagnosticsTelemetry.SessionDialoguesCount;
                        obj["lastInterlocutor"] = DiagnosticsTelemetry.LastInterlocutor;
                        obj["logs"] = JArray.FromObject(DiagnosticsTelemetry.GetRecentLogs());

                        await SendJsonResponseAsync(resp, obj.ToString(Formatting.None)).ConfigureAwait(false);
                        return;
                    }
                    else if (string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        string body;
                        using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                        {
                            body = await reader.ReadToEndAsync().ConfigureAwait(false);
                        }

                        JObject payload = JObject.Parse(body);
                        Profile cur = ProfileBook.Current;

                        // General settings
                        JObject general = payload["general"] as JObject;
                        if (general != null)
                        {
                            if (general["defaultProvider"] != null && Profile.TryParseProvider((string)general["defaultProvider"], out var defKind))
                            {
                                cur.SwitchProvider(defKind);
                            }
                            if (general["logLevel"] != null)
                            {
                                cur.LogEnabled = !string.Equals((string)general["logLevel"], "Off", StringComparison.OrdinalIgnoreCase);
                            }
                        }

                        // Providers dictionary
                        JObject providers = payload["providers"] as JObject;
                        if (providers != null)
                        {
                            if (cur.Providers == null)
                            {
                                cur.Providers = new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);
                            }

                            foreach (var prop in providers.Properties())
                            {
                                string provName = prop.Name;
                                JObject pObj = prop.Value as JObject;
                                if (pObj != null)
                                {
                                    if (!cur.Providers.TryGetValue(provName, out ProviderSettings ps) || ps == null)
                                    {
                                        ps = new ProviderSettings();
                                        cur.Providers[provName] = ps;
                                    }

                                    if (pObj["apiKey"] != null) ps.ApiKey = (string)pObj["apiKey"];
                                    if (pObj["model"] != null) ps.Model = (string)pObj["model"];
                                    if (pObj["endpoint"] != null) ps.RootUrl = (string)pObj["endpoint"];

                                    string activeProvLabel = Profile.Label(cur.Provider);
                                    if (string.Equals(provName, activeProvLabel, StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (pObj["apiKey"] != null) cur.ApiKey = (string)pObj["apiKey"];
                                        if (pObj["model"] != null) cur.Model = (string)pObj["model"];
                                        if (pObj["endpoint"] != null) cur.RootUrl = (string)pObj["endpoint"];
                                    }

                                    if (string.Equals(provName, "Antigravity", StringComparison.OrdinalIgnoreCase))
                                    {
                                        if (pObj["endpoint"] != null) cur.AntigravityCliPath = (string)pObj["endpoint"];
                                        if (pObj["model"] != null) cur.AntigravityModel = (string)pObj["model"];
                                    }
                                }
                            }
                        }

                        // Scopes dictionary
                        JObject scopes = payload["scopes"] as JObject;
                        if (scopes != null)
                        {
                            foreach (var prop in scopes.Properties())
                            {
                                ScopeId scId = ParseScope(prop.Name);
                                if (scId != ScopeId.None)
                                {
                                    var scSettings = cur.Scope(scId);
                                    JObject scObj = prop.Value as JObject;
                                    if (scObj != null)
                                    {
                                        if (scObj["enabled"] != null) scSettings.Enabled = (bool)scObj["enabled"];
                                        if (scObj["provider"] != null && Profile.TryParseProvider((string)scObj["provider"], out var scKind))
                                        {
                                            scSettings.Provider = scKind;
                                        }
                                        if (scObj["model"] != null) scSettings.Model = (string)scObj["model"];
                                        if (scObj["temperature"] != null && double.TryParse(scObj["temperature"].ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double st)) scSettings.Temperature = st;
                                        if (scObj["maxTokens"] != null)
                                        {
                                            if (int.TryParse(scObj["maxTokens"].ToString(), out int mt))
                                                scSettings.MaxTokens = mt > 0 ? mt : 0;
                                            else
                                                scSettings.MaxTokens = 0;
                                        }
                                        if (scObj["systemPrompt"] != null) scSettings.SystemPrompt = (string)scObj["systemPrompt"];
                                    }
                                }
                            }
                        }

                        // Legacy root fields
                        if (payload["masterEnabled"] != null) cur.MasterEnabled = (bool)payload["masterEnabled"];
                        if (payload["provider"] != null && Profile.TryParseProvider((string)payload["provider"], out var rootKind))
                        {
                            cur.SwitchProvider(rootKind);
                        }
                        if (payload["rootUrl"] != null) cur.RootUrl = (string)payload["rootUrl"];
                        if (payload["apiKey"] != null) cur.ApiKey = (string)payload["apiKey"];
                        if (payload["model"] != null) cur.Model = (string)payload["model"];
                        if (payload["temperature"] != null) cur.Temperature = (double)payload["temperature"];
                        if (payload["maxTokens"] != null) cur.MaxTokens = (int)payload["maxTokens"];
                        if (payload["timeoutSeconds"] != null) cur.TimeoutSeconds = (int)payload["timeoutSeconds"];

                        ProfileBook.Save();

                        try
                        {
                            McmProviderSettings.Instance?.PullFromProfile();
                            McmScopeSettings.Instance?.PullFromProfile();
                        }
                        catch { }

                        LogSink.Always("[PrismWebUiHost] Settings saved via WebUI.");
                        await SendJsonResponseAsync(resp, "{\"ok\":true,\"success\":true,\"message\":\"Settings saved\"}").ConfigureAwait(false);
                        return;
                    }
                }

                if ((path == "/api/telemetry" || path == "/api/state") && string.Equals(req.HttpMethod, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    Profile cur = ProfileBook.Current;
                    int activeScopesCount = 0;
                    if (cur != null && cur.Scopes != null)
                    {
                        foreach (var sc in cur.Scopes.Values)
                        {
                            if (sc != null && sc.Enabled) activeScopesCount++;
                        }
                    }
                    if (activeScopesCount == 0) activeScopesCount = 8;

                    var telemetryObj = new JObject
                    {
                        ["ok"] = true,
                        ["sessionDialogues"] = DiagnosticsTelemetry.SessionDialoguesCount,
                        ["lastInterlocutor"] = DiagnosticsTelemetry.LastInterlocutor,
                        ["activeScopes"] = activeScopesCount,
                        ["bclActive"] = true,
                        ["logs"] = JArray.FromObject(DiagnosticsTelemetry.GetRecentLogs())
                    };
                    await SendJsonResponseAsync(resp, telemetryObj.ToString(Formatting.None)).ConfigureAwait(false);
                    return;
                }

                if (path == "/api/trigger/event" && string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    bool triggered = false;
                    string errorMsg = null;
                    try
                    {
                        Type mgrType = Type.GetType("AIInfluence.DynamicEvents.DynamicEventsManager, AIInfluence")
                            ?? HarmonyLib.AccessTools.TypeByName("AIInfluence.DynamicEvents.DynamicEventsManager");

                        if (mgrType != null)
                        {
                            object instance = mgrType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                            if (instance != null)
                            {
                                MethodInfo method = mgrType.GetMethod("GenerateEventsManually", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (method != null)
                                {
                                    method.Invoke(instance, null);
                                    triggered = true;
                                }
                            }
                        }

                        if (!triggered)
                        {
                            Type genType = Type.GetType("AIInfluence.DynamicEvents.DynamicEventsGenerator, AIInfluence")
                                ?? HarmonyLib.AccessTools.TypeByName("AIInfluence.DynamicEvents.DynamicEventsGenerator");
                            if (genType != null)
                            {
                                object genInstance = Activator.CreateInstance(genType);
                                MethodInfo genMethod = genType.GetMethod("GenerateEvents", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                                if (genMethod != null)
                                {
                                    genMethod.Invoke(genInstance, null);
                                    triggered = true;
                                }
                            }
                        }

                        LogSink.Always("[PrismWebUiHost] Triggered dynamic event manually (triggered=" + triggered + ").");
                        DiagnosticsTelemetry.RecordEvent("Calradia", "Manual dynamic event triggered via WebUI");
                    }
                    catch (Exception ex)
                    {
                        errorMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                        LogSink.Error("[PrismWebUiHost] Failed to trigger dynamic event: " + errorMsg, ex);
                    }

                    var resObj = new JObject
                    {
                        ["ok"] = errorMsg == null,
                        ["success"] = errorMsg == null,
                        ["message"] = errorMsg == null ? "Random dynamic event triggered in Calradia!" : errorMsg,
                        ["triggeredInGame"] = triggered
                    };
                    await SendJsonResponseAsync(resp, resObj.ToString(Formatting.None)).ConfigureAwait(false);
                    return;
                }

                if (path == "/api/trigger/diplomacy" && string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    bool dispatched = false;
                    string errorMsg = null;
                    string statement = "Мы заявляем о наших дипломатических намерениях в Кальрадии и стремимся укрепить баланс сил.";

                    try
                    {
                        if (req.HasEntityBody)
                        {
                            using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                            {
                                string b = await reader.ReadToEndAsync().ConfigureAwait(false);
                                if (!string.IsNullOrWhiteSpace(b))
                                {
                                    try
                                    {
                                        JObject bodyObj = JObject.Parse(b);
                                        string text = (string)bodyObj["statement"] ?? (string)bodyObj["prompt"] ?? (string)bodyObj["text"];
                                        if (!string.IsNullOrWhiteSpace(text)) statement = text;
                                    }
                                    catch
                                    {
                                        statement = b.Trim();
                                    }
                                }
                            }
                        }

                        Type diploType = Type.GetType("AIInfluence.Diplomacy.DiplomacyManager, AIInfluence")
                            ?? HarmonyLib.AccessTools.TypeByName("AIInfluence.Diplomacy.DiplomacyManager");

                        if (diploType != null)
                        {
                            object instance = diploType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
                            if (instance != null)
                            {
                                MethodInfo procMethod = diploType.GetMethod("ProcessPlayerStatement", new[] { typeof(string) });
                                if (procMethod != null)
                                {
                                    procMethod.Invoke(instance, new object[] { statement });
                                    dispatched = true;
                                }
                            }
                        }

                        LogSink.Always("[PrismWebUiHost] Diplomatic statement dispatched (dispatched=" + dispatched + "): " + statement);
                        DiagnosticsTelemetry.RecordDiplomacy(statement, "Kingdoms");
                    }
                    catch (Exception ex)
                    {
                        errorMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                        LogSink.Error("[PrismWebUiHost] Failed to dispatch diplomatic statement: " + errorMsg, ex);
                    }

                    var resObj = new JObject
                    {
                        ["ok"] = errorMsg == null,
                        ["success"] = errorMsg == null,
                        ["message"] = errorMsg == null ? "Diplomatic statement dispatched to kingdoms!" : errorMsg,
                        ["dispatchedInGame"] = dispatched
                    };
                    await SendJsonResponseAsync(resp, resObj.ToString(Formatting.None)).ConfigureAwait(false);
                    return;
                }

                if (path == "/api/test" && string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    string body;
                    using (var reader = new StreamReader(req.InputStream, Encoding.UTF8))
                    {
                        body = await reader.ReadToEndAsync().ConfigureAwait(false);
                    }

                    JObject payload = JObject.Parse(body);
                    string prompt = (string)payload["prompt"];
                    if (string.IsNullOrWhiteSpace(prompt) || prompt == "Тестовое приветствие")
                    {
                        prompt = "Если ты видишь это сообщение - напиши текущее время и Соединение успешно";
                    }
                    string scopeStr = (string)payload["scope"];
                    ScopeId targetScope = ParseScope(scopeStr);
                    if (targetScope == ScopeId.None)
                    {
                        targetScope = ScopeId.DialogueHeroes;
                    }

                    string provStr = (string)payload["provider"];
                    string modelStr = (string)payload["model"];
                    string apiKeyStr = (string)payload["apiKey"];
                    string endpointStr = (string)payload["endpoint"] ?? (string)payload["rootUrl"];

                    Stopwatch sw = Stopwatch.StartNew();
                    try
                    {
                        string reply;
                        Profile cur = ProfileBook.Current;

                        if (!string.IsNullOrWhiteSpace(provStr) && Profile.TryParseProvider(provStr, out var provKind))
                        {
                            Profile testProfile = cur.CloneWithModel(modelStr ?? cur.Model);
                            testProfile.Provider = provKind;
                            if (!string.IsNullOrWhiteSpace(apiKeyStr)) testProfile.ApiKey = apiKeyStr;
                            if (!string.IsNullOrWhiteSpace(endpointStr)) testProfile.RootUrl = endpointStr;
                            if (!string.IsNullOrWhiteSpace(modelStr))
                            {
                                testProfile.Model = modelStr;
                                if (provKind == ProviderKind.Antigravity) testProfile.AntigravityModel = modelStr;
                            }
                            if (!string.IsNullOrWhiteSpace(endpointStr) && provKind == ProviderKind.Antigravity)
                            {
                                testProfile.AntigravityCliPath = endpointStr;
                            }

                            IChatTransport transport = Pipeline.Resolve(provKind);
                            ChatTurn turn = ChatTurn.FromPrompt(prompt);
                            turn.Scope = targetScope;
                            turn.SplitUser = prompt;
                            ModelOptions options = ModelOptions.FromProfile(testProfile);
                            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Math.Max(10, testProfile.TimeoutSeconds))))
                            {
                                reply = await transport.SendAsync(turn, options, testProfile, cts.Token).ConfigureAwait(false);
                            }
                        }
                        else
                        {
                            reply = await Pipeline.SendAsync(targetScope, prompt, null, prompt, null).ConfigureAwait(false);
                        }

                        sw.Stop();

                        var res = new JObject
                        {
                            ["ok"] = true,
                            ["success"] = true,
                            ["reply"] = reply,
                            ["response"] = reply,
                            ["latencyMs"] = sw.ElapsedMilliseconds,
                            ["elapsedMs"] = sw.ElapsedMilliseconds,
                            ["tokens"] = Math.Max(1, (reply ?? string.Empty).Length / 4)
                        };
                        await SendJsonResponseAsync(resp, res.ToString(Formatting.None)).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        var res = new JObject
                        {
                            ["ok"] = false,
                            ["success"] = false,
                            ["error"] = ex.Message,
                            ["latencyMs"] = sw.ElapsedMilliseconds,
                            ["elapsedMs"] = sw.ElapsedMilliseconds
                        };
                        await SendJsonResponseAsync(resp, res.ToString(Formatting.None)).ConfigureAwait(false);
                    }
                    return;
                }

                if (path == "/api/close" && string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    Close();
                    await SendJsonResponseAsync(resp, "{\"ok\":true,\"success\":true}").ConfigureAwait(false);
                    return;
                }

                if (path == "/api/open-external" && string.Equals(req.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    LaunchExternalBrowser();
                    await SendJsonResponseAsync(resp, "{\"ok\":true,\"success\":true}").ConfigureAwait(false);
                    return;
                }

                // Fallback: 404
                resp.StatusCode = (int)HttpStatusCode.NotFound;
                byte[] notFound = Encoding.UTF8.GetBytes("Not Found");
                await resp.OutputStream.WriteAsync(notFound, 0, notFound.Length).ConfigureAwait(false);
                resp.Close();
            }
            catch (Exception ex)
            {
                LogSink.Error("[PrismWebUiHost] Request processing error: " + ex.Message, ex);
                try
                {
                    ctx.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    ctx.Response.Close();
                }
                catch { }
            }
        }

        private static string GetHtmlContent()
        {
            try
            {
                string[] candidatePaths = new string[]
                {
                    Path.Combine(ModuleLocator.Root ?? string.Empty, "wwwroot", "index.html"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Modules", "AIInfluencePrism", "wwwroot", "index.html"),
                    @"Y:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\AIInfluencePrism\wwwroot\index.html",
                    @"Y:\Prism 1.0.0\wwwroot\index.html",
                    @"Y:\Prism Settings Design Ideas\deliverables\prism-imperial-archive.html"
                };

                foreach (string candidate in candidatePaths)
                {
                    if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    {
                        string html = File.ReadAllText(candidate, Encoding.UTF8);
                        if (!string.IsNullOrWhiteSpace(html))
                        {
                            if (!html.Contains("btn-trigger-event"))
                            {
                                html = InjectActionButtons(html);
                            }
                            return html;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogSink.Warn("[PrismWebUiHost] Failed reading index.html from disk: " + ex.Message);
            }

            string embedded = PrismWebUiHtml.GetHtml();
            if (!embedded.Contains("btn-trigger-event"))
            {
                embedded = InjectActionButtons(embedded);
            }
            return embedded;
        }

        private static string InjectActionButtons(string html)
        {
            string sandboxScript = @"
    <script>
      function triggerInGameAction(endpoint, label) {
        var btn = (typeof event !== 'undefined' && event) ? event.currentTarget : null;
        var origText = btn ? btn.innerText : '';
        if (btn) { btn.innerText = label; btn.disabled = true; }
        fetch(endpoint, { method: 'POST' })
          .then(function(r) { return r.json(); })
          .then(function(data) {
            alert(data.message || (data.success || data.ok ? 'Action successfully sent to Calradia!' : 'Error: ' + (data.error || 'unknown')));
          })
          .catch(function(err) {
            alert('Connection error: ' + err);
          })
          .finally(function() {
            if (btn) { btn.innerText = origText; btn.disabled = false; }
          });
      }

      function ensureSandboxActions() {
        var sandboxSettings = document.querySelector('.sandbox-settings') || document.querySelector('.card:last-child');
        if (!sandboxSettings || document.getElementById('calradia-actions-card')) return;

        var card = document.createElement('div');
        card.id = 'calradia-actions-card';
        card.style.cssText = 'margin-top:16px;padding:14px;border:1px solid #d97706;border-radius:8px;background:rgba(217,119,6,0.08);';
        card.innerHTML =
          '<div style=""font-size:11px;font-weight:700;color:#fcd34d;margin-bottom:10px;text-transform:uppercase;letter-spacing:1px;display:flex;align-items:center;gap:6px;"">' +
            '<span>⚡ Live Calradia Actions</span>' +
          '</div>' +
          '<div style=""display:flex;flex-direction:column;gap:8px;"">' +
            '<button id=""btn-trigger-event"" style=""cursor:pointer;background:#221c14;color:#fcd34d;border:1px solid #d97706;padding:9px 14px;border-radius:6px;font-size:12px;font-weight:700;display:flex;align-items:center;gap:8px;transition:all 0.2s;"" onmouseover=""this.style.background=\'#342817\'"" onmouseout=""this.style.background=\'#221c14\'"" onclick=""triggerInGameAction(\'/api/trigger/event\', \'Triggering...\')"">' +
              '<span>⚡ Trigger Random Dynamic Event</span>' +
            '</button>' +
            '<button id=""btn-trigger-diplomacy"" style=""cursor:pointer;background:#221c14;color:#60a5fa;border:1px solid #2563eb;padding:9px 14px;border-radius:6px;font-size:12px;font-weight:700;display:flex;align-items:center;gap:8px;transition:all 0.2s;"" onmouseover=""this.style.background=\'#1f2b3e\'"" onmouseout=""this.style.background=\'#221c14\'"" onclick=""triggerInGameAction(\'/api/trigger/diplomacy\', \'Dispatching...\')"">' +
              '<span>📜 Trigger Diplomatic Statement</span>' +
            '</button>' +
          '</div>';

        sandboxSettings.appendChild(card);
      }
      setInterval(ensureSandboxActions, 500);
    </script>
";
            if (html.Contains("</body>"))
            {
                return html.Replace("</body>", sandboxScript + "</body>");
            }
            return html + sandboxScript;
        }

        private static async Task SendJsonResponseAsync(HttpListenerResponse resp, string json)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(json);
            resp.ContentType = "application/json; charset=utf-8";
            resp.ContentLength64 = bytes.Length;
            resp.StatusCode = (int)HttpStatusCode.OK;
            await resp.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            resp.Close();
        }

        public static string GetCanonicalScopeName(ScopeId sc)
        {
            switch (sc)
            {
                case ScopeId.DialogueHeroes: return "DialogueHeroes";
                case ScopeId.DialogueTownsfolk: return "DialogueTownsfolk";
                case ScopeId.NpcLetters: return "NpcLetters";
                case ScopeId.NpcInitiative: return "Initiative";
                case ScopeId.CharacterDevelopment: return "CharacterDevelopment";
                case ScopeId.BattleTactics: return "Tactics";
                case ScopeId.Diplomacy: return "Diplomacy";
                case ScopeId.MemoryBook: return "MemoryBook";
                default: return sc.ToString();
            }
        }

        public static ScopeId ParseScope(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return ScopeId.None;
            string s = name.Trim();
            if (string.Equals(s, "DialogueHeroes", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Dialogue", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "heroes", StringComparison.OrdinalIgnoreCase))
                return ScopeId.DialogueHeroes;
            if (string.Equals(s, "DialogueTownsfolk", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Townsfolk", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "townsfolk", StringComparison.OrdinalIgnoreCase))
                return ScopeId.DialogueTownsfolk;
            if (string.Equals(s, "NpcLetters", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "npc-letters", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Letters", StringComparison.OrdinalIgnoreCase))
                return ScopeId.NpcLetters;
            if (string.Equals(s, "NpcInitiative", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "npc-initiative", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Initiative", StringComparison.OrdinalIgnoreCase))
                return ScopeId.NpcInitiative;
            if (string.Equals(s, "CharacterDevelopment", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "character-development", StringComparison.OrdinalIgnoreCase))
                return ScopeId.CharacterDevelopment;
            if (string.Equals(s, "BattleTactics", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "battle-tactics", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Tactics", StringComparison.OrdinalIgnoreCase))
                return ScopeId.BattleTactics;
            if (string.Equals(s, "Diplomacy", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "diplomacy", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "Events", StringComparison.OrdinalIgnoreCase))
                return ScopeId.Diplomacy;
            if (string.Equals(s, "MemoryBook", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "memory-book", StringComparison.OrdinalIgnoreCase))
                return ScopeId.MemoryBook;

            if (Enum.TryParse<ScopeId>(s, true, out var parsed))
                return parsed;

            return ScopeId.None;
        }
    }
}

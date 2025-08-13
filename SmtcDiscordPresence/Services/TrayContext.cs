using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SmtcDiscordPresence.Services;
using SmtcDiscordPresence.Models;

namespace SmtcDiscordPresence
{
    public sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _tray = new();
        private readonly MediaSessionService _media = new();
        private readonly DiscordPresenceService _discord = new();
        private System.Threading.Timer? _timer;
        private readonly AppConfiguration? _config;

        public TrayContext()
        {            
            DebugLogger.WriteLine("[TrayContext] Starting TrayContext initialization...");
            
            try
            {
                _config = ConfigurationService.LoadConfiguration();
                DebugLogger.WriteLine($"[TrayContext] Configuration loaded successfully. Discord Client ID: {_config.Discord.ClientId}");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[TrayContext] Configuration error: {ex.Message}");
                MessageBox.Show($"Configuration Error: {ex.Message}", "SmtcDiscordPresence", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                ExitThread();
                return;
            }

            // Tray icon and menu
            _tray.Icon = SystemIcons.Application; // replace with your resource icon if available
            _tray.Text = "SMTC → Discord Presence";
            _tray.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show Debug Log", null, (_, __) => DebugLogger.ShowLogWindow());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Force Refresh Now", null, async (_, __) => await RefreshAsync());
            menu.Items.Add("Check Discord Status", null, (_, __) => CheckDiscordStatus());
            menu.Items.Add("Pause updates", null, (_, __) => TogglePause());
            menu.Items.Add("Reconnect Discord", null, async (_, __) => await ReconnectDiscordAsync());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (_, __) => ExitThread());
            _tray.ContextMenuStrip = menu;

            DebugLogger.WriteLine("[TrayContext] Tray icon and menu setup complete.");

            // Initialize
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            if (_config == null) 
            {
                DebugLogger.WriteLine("[TrayContext] Configuration is null, aborting initialization.");
                return;
            }
            
            try
            {
                DebugLogger.WriteLine("[TrayContext] 🚀 Starting service initialization...");
                await _media.InitializeAsync();
                
                DebugLogger.WriteLine("[TrayContext] 🔗 Initializing Discord connection...");
                _discord.Initialize(_config.Discord.ClientId);
                
                // Wait a bit for Discord to connect before starting updates
                await Task.Delay(3000);
                
                DebugLogger.WriteLine("[TrayContext] 📺 Setting up media change events...");
                _media.SnapshotChanged += async (_, __) => {
                    DebugLogger.WriteLine("[TrayContext] 📻 Media snapshot changed event fired.");
                    await RefreshAsync();
                };
                
                var intervalMs = _config.Settings.UpdateIntervalSeconds * 1000;
                DebugLogger.WriteLine($"[TrayContext] ⏰ Setting up timer with interval: {intervalMs}ms");
                _timer = new System.Threading.Timer(async _ => await RefreshAsync(), null, 0, intervalMs);
                
                DebugLogger.WriteLine("[TrayContext] ✅ Initialization completed successfully.");
                ShowBalloon("Started", "SMTC Discord Presence started successfully.", ToolTipIcon.Info);
                
                // Do an initial refresh
                await RefreshAsync();
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[TrayContext] ❌ Initialization error: {ex.Message}");
                ShowBalloon("Initialization failed", ex.Message, ToolTipIcon.Error);
            }
        }

        private bool _paused;
        private void TogglePause()
        {
            _paused = !_paused;
            DebugLogger.WriteLine($"[TrayContext] Toggled pause state: {(_paused ? "PAUSED" : "RESUMED")}");
            ShowBalloon(_paused ? "Paused" : "Resumed",
                        _paused ? "Presence updates are paused." : "Presence updates resumed.",
                        ToolTipIcon.Info);
            if (_paused) _discord.Clear();
        }

        private async Task RefreshAsync()
        {
            if (_paused) 
            {
                DebugLogger.WriteLine("[TrayContext] Refresh skipped - application is paused.");
                return;
            }
            
            try
            {
                DebugLogger.WriteLine("[TrayContext] Starting media refresh...");
                var snap = await _media.GetSnapshotAsync();
                
                if (snap == null)
                {
                    DebugLogger.WriteLine("[TrayContext] No media snapshot available - clearing Discord presence.");
                }
                else
                {
                    DebugLogger.WriteLine($"[TrayContext] Got media snapshot - sending to Discord: '{snap.Title}' by '{snap.Artist}'");
                }
                
                await _discord.SetPresenceAsync(snap);
                DebugLogger.WriteLine("[TrayContext] Discord presence update completed.");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[TrayContext] Error during refresh: {ex.Message}");
            }
        }

        private async Task ReconnectDiscordAsync()
        {
            if (_config == null) 
            {
                DebugLogger.WriteLine("[TrayContext] Cannot reconnect Discord - configuration not available.");
                return;
            }
            
            DebugLogger.WriteLine("[TrayContext] 🔄 Reconnecting Discord...");
            _discord.Dispose();
            await Task.Delay(1000); // Give more time for cleanup
            _discord.Initialize(_config.Discord.ClientId);
            await Task.Delay(3000); // Give time for connection
            await RefreshAsync();
            DebugLogger.WriteLine("[TrayContext] ✅ Discord reconnection completed.");
            ShowBalloon("Reconnected", "Discord connection reestablished.", ToolTipIcon.Info);
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon)
        {
            if (_config?.Settings.ShowBalloonTips == true)
            {
                DebugLogger.WriteLine($"[TrayContext] Showing balloon: {title} - {text}");
                _tray.ShowBalloonTip(3000, title, text, icon);
            }
        }

        private void CheckDiscordStatus()
        {
            DebugLogger.WriteLine("[TrayContext] 🔍 Checking Discord status...");
            var isReady = _discord.IsReady;
            var message = $"Discord Connection Status: {(isReady ? "✅ Connected" : "❌ Not Connected")}";
            
            DebugLogger.WriteLine($"[TrayContext] {message}");
            ShowBalloon("Discord Status", isReady ? "Connected and ready" : "Not connected - check if Discord is running", 
                       isReady ? ToolTipIcon.Info : ToolTipIcon.Warning);
            
            if (!isReady)
            {
                DebugLogger.WriteLine("[TrayContext] 💡 Tip: Make sure Discord is running, then try 'Reconnect Discord'");
            }
        }

        protected override void ExitThreadCore()
        {
            DebugLogger.WriteLine("[TrayContext] Shutting down...");
            _timer?.Dispose();
            _discord.Dispose();
            _tray.Visible = false;
            _tray.Dispose();
            DebugLogger.Dispose();
            base.ExitThreadCore();
            DebugLogger.WriteLine("[TrayContext] Shutdown complete.");
        }
    }
}
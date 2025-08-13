using System;
using System.Threading.Tasks;
using DiscordRPC;
using SmtcDiscordPresence.Services;

namespace SmtcDiscordPresence.Services
{
    public sealed class DiscordPresenceService : IDisposable
    {
        private DiscordRpcClient? _client;
        private DateTime _lastPushUtc = DateTime.MinValue;
        private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(3);
        private bool _isConnected = false;

        public bool IsReady => _client?.IsInitialized == true && _isConnected;

        public void Initialize(string clientId)
        {
            DebugLogger.WriteLine($"[Discord] Initializing Discord client with ID: {clientId}");
            
            try
            {
                _client = new DiscordRpcClient(clientId)
                {
                    SkipIdenticalPresence = false
                };

                _client.OnReady += (sender, e) =>
                {
                    _isConnected = true;
                    DebugLogger.WriteLine($"[Discord] ✅ Discord client ready! User: {e.User.Username}");
                };

                _client.OnPresenceUpdate += (sender, e) =>
                {
                    DebugLogger.WriteLine($"[Discord] ✅ Presence updated successfully");
                };

                _client.OnError += (sender, e) =>
                {
                    DebugLogger.WriteLine($"[Discord] ❌ Discord error: {e.Message}");
                };

                _client.OnConnectionFailed += (sender, e) =>
                {
                    _isConnected = false;
                    DebugLogger.WriteLine($"[Discord] ❌ Connection failed: {e.Type}");
                };

                _client.OnConnectionEstablished += (sender, e) =>
                {
                    _isConnected = true;
                    DebugLogger.WriteLine($"[Discord] ✅ Connection established");
                };

                _client.OnClose += (sender, e) =>
                {
                    _isConnected = false;
                    DebugLogger.WriteLine($"[Discord] ❌ Connection closed: {e.Reason}");
                };

                DebugLogger.WriteLine("[Discord] Starting Discord client initialization...");
                _client.Initialize();

                Task.Delay(3000).ContinueWith(_ =>
                {
                    DebugLogger.WriteLine($"[Discord] Connection status after 3s - Connected: {_isConnected}");
                    if (!_isConnected)
                    {
                        DebugLogger.WriteLine("[Discord] ⚠️ Discord not connected. Make sure Discord is running.");
                    }
                });
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Discord] ❌ Failed to initialize Discord client: {ex.Message}");
                throw;
            }
        }

        public async Task SetPresenceAsync(MediaSnapshot? snap)
        {
            if (_client == null || !_client.IsInitialized || !_isConnected)
            {
                DebugLogger.WriteLine("[Discord] ❌ Cannot set presence - Discord not ready");
                return;
            }

            var timeSinceLastPush = DateTime.UtcNow - _lastPushUtc;
            if (timeSinceLastPush < _minInterval) 
            {
                return; // Rate limiting
            }
            
            _lastPushUtc = DateTime.UtcNow;

            if (snap == null || string.IsNullOrWhiteSpace(snap.Title))
            {
                DebugLogger.WriteLine("[Discord] 🧹 Clearing presence");
                _client.ClearPresence();
                return;
            }

            try
            {
                var presence = new RichPresence
                {
                    Details = Truncate(snap.Title, 128),
                    State = !string.IsNullOrEmpty(snap.Artist) ? Truncate(snap.Artist, 128) : "Unknown Artist"
                };

                DebugLogger.WriteLine($"[Discord] 🎵 Setting presence: '{presence.Details}' by '{presence.State}'");
                _client.SetPresence(presence);
                DebugLogger.WriteLine("[Discord] ✅ Presence sent to Discord!");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Discord] ❌ Error setting presence: {ex.Message}");
            }
            
            await Task.CompletedTask;
        }

        public void Clear() 
        {
            try
            {
                _client?.ClearPresence();
                DebugLogger.WriteLine("[Discord] ✅ Presence cleared");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Discord] ❌ Error clearing presence: {ex.Message}");
            }
        }

        public void Dispose()
        {
            try 
            { 
                _client?.Dispose(); 
                _isConnected = false;
                DebugLogger.WriteLine("[Discord] ✅ Discord client disposed");
            } 
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Discord] ❌ Error disposing: {ex.Message}");
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s.Substring(0, max));
    }
}
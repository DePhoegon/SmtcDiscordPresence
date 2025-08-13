using System;
using System.Threading.Tasks;
using DiscordRPC;
using SmtcDiscordPresence.Services;
using Windows.Media.Control;

namespace SmtcDiscordPresence.Services
{
    public sealed class DiscordPresenceService : IDisposable
    {
        private DiscordRpcClient? _client;
        private DateTime _lastPushUtc = DateTime.MinValue;
        private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(2); // Reduced from 3 to 2 seconds for faster updates
        private bool _isConnected = false;

        public bool IsReady => _client?.IsInitialized == true && _isConnected;

        public void Initialize(string clientId)
        {
            DebugLogger.WriteLine($"[Discord] Initializing Discord client with ID: {clientId}");
            
            try
            {
                _client = new DiscordRpcClient(clientId)
                {
                    SkipIdenticalPresence = false // Ensure we don't skip updates
                };

                _client.OnReady += (sender, e) =>
                {
                    _isConnected = true;
                    DebugLogger.WriteLine($"[Discord] ✅ Discord client ready! User: {e.User.Username}");
                    DebugLogger.WriteLine($"[Discord] 🔧 Client settings - SkipIdenticalPresence: {_client.SkipIdenticalPresence}");
                };

                _client.OnPresenceUpdate += (sender, e) =>
                {
                    DebugLogger.WriteLine($"[Discord] ✅ Presence updated successfully");
                    DebugLogger.WriteLine($"[Discord] 📋 Current presence:");
                    DebugLogger.WriteLine($"[Discord]   Details: '{e.Presence?.Details}'");
                    DebugLogger.WriteLine($"[Discord]   State: '{e.Presence?.State}'");
                };

                _client.OnError += (sender, e) =>
                {
                    DebugLogger.WriteLine($"[Discord] ❌ Discord error: {e.Message}");
                    DebugLogger.WriteLine($"[Discord] ❌ Error code: {e.Code}");
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
                    DebugLogger.WriteLine($"[Discord] 🔗 Connection type: {e.ConnectedPipe}");
                };

                _client.OnClose += (sender, e) =>
                {
                    _isConnected = false;
                    DebugLogger.WriteLine($"[Discord] ❌ Connection closed: {e.Reason}");
                    DebugLogger.WriteLine($"[Discord] 🔍 Close code: {e.Code}");
                };

                DebugLogger.WriteLine("[Discord] Starting Discord client initialization...");
                _client.Initialize();

                Task.Delay(3000).ContinueWith(_ =>
                {
                    DebugLogger.WriteLine($"[Discord] Connection status after 3s - Connected: {_isConnected}");
                    if (!_isConnected)
                    {
                        DebugLogger.WriteLine("[Discord] ⚠️ Discord not connected. Make sure Discord is running.");
                        DebugLogger.WriteLine("[Discord] 💡 Try restarting Discord and this application.");
                    }
                    else
                    {
                        DebugLogger.WriteLine("[Discord] ✅ Discord connection established successfully!");
                    }
                });
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Discord] ❌ Failed to initialize Discord client: {ex.Message}");
                DebugLogger.WriteLine($"[Discord] ❌ Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        public async Task SetPresenceAsync(MediaSnapshot? snap)
        {
            if (_client == null || !_client.IsInitialized || !_isConnected)
            {
                DebugLogger.WriteLine("[Discord] ❌ Cannot set presence - Discord not ready");
                DebugLogger.WriteLine($"[Discord] Debug: _client null: {_client == null}, Initialized: {_client?.IsInitialized}, Connected: {_isConnected}");
                return;
            }

            var timeSinceLastPush = DateTime.UtcNow - _lastPushUtc;
            if (timeSinceLastPush < _minInterval) 
            {
                DebugLogger.WriteLine($"[Discord] ⏸️ Rate limited - {timeSinceLastPush.TotalSeconds:F1}s since last update (min: {_minInterval.TotalSeconds}s)");
                return; // Rate limiting
            }
            
            _lastPushUtc = DateTime.UtcNow;

            if (snap == null || string.IsNullOrWhiteSpace(snap.Title))
            {
                DebugLogger.WriteLine("[Discord] 🧹 Clearing presence - no media content");
                _client.ClearPresence();
                return;
            }

            // Clear presence on pause - act as if no video or music is playing
            if (snap.Status == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
            {
                DebugLogger.WriteLine("[Discord] ⏸️ Content is paused - clearing presence (acting as if no media is playing)");
                _client.ClearPresence();
                return;
            }

            try
            {
                DebugLogger.WriteLine($"[Discord] 📋 Media snapshot details:");
                DebugLogger.WriteLine($"[Discord]   Type: {snap.Type}");
                DebugLogger.WriteLine($"[Discord]   VideoSource: {snap.VideoSource}");
                DebugLogger.WriteLine($"[Discord]   AudioSource: {snap.AudioSource}");
                DebugLogger.WriteLine($"[Discord]   Title: '{snap.Title}'");
                DebugLogger.WriteLine($"[Discord]   Artist: '{snap.Artist}'");
                DebugLogger.WriteLine($"[Discord]   Album: '{snap.Album}'");
                DebugLogger.WriteLine($"[Discord]   SourceApp: '{snap.SourceApp}'");

                // Create different presence based on media type and video source
                var presence = snap.Type switch
                {
                    MediaType.Audio => CreateAudioPresence(snap),
                    MediaType.Video => CreateVideoPresence(snap),
                    _ => CreateGenericVideoPresenceWithTimestamps(snap)
                };

                var sourceIcon = GetSourceIcon(snap);
                DebugLogger.WriteLine($"[Discord] {sourceIcon} Setting {snap.Type} presence for {snap.VideoSource}:");
                DebugLogger.WriteLine($"[Discord]   Details: '{presence.Details}'");
                DebugLogger.WriteLine($"[Discord]   State: '{presence.State}'");
                DebugLogger.WriteLine($"[Discord]   Has Timestamps: {presence.Timestamps != null}");
                
                _client.SetPresence(presence);
                DebugLogger.WriteLine("[Discord] ✅ Presence sent to Discord client!");
                DebugLogger.WriteLine($"[Discord] 🔄 Forcing presence update with SkipIdenticalPresence: {_client.SkipIdenticalPresence}");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Discord] ❌ Error setting presence: {ex.Message}");
                DebugLogger.WriteLine($"[Discord] ❌ Stack trace: {ex.StackTrace}");
            }
            
            await Task.CompletedTask;
        }

        private string GetSourceIcon(MediaSnapshot snap)
        {
            return snap.VideoSource switch
            {
                VideoSourceType.YouTube => "📺🔴",
                VideoSourceType.Netflix => "📺🎬",
                VideoSourceType.Twitch => "📺💜",
                VideoSourceType.PrimeVideo => "📺📦",
                VideoSourceType.Hulu => "📺🟢",
                VideoSourceType.Disney => "📺🏰",
                VideoSourceType.LocalVideo => "📺💾",
                VideoSourceType.Generic => "📺🌐",
                _ when snap.Type == MediaType.Audio => snap.AudioSource switch
                {
                    AudioSourceType.YouTubeMusic => "🎵🔴",
                    AudioSourceType.Spotify => "🎵🟢",
                    AudioSourceType.Generic => "🎵🌐",
                    AudioSourceType.LocalAudio => "🎵💾",
                    _ => "🎵"
                },
                _ => "❓"
            };
        }

        private RichPresence CreateAudioPresence(MediaSnapshot snap)
        {
            DebugLogger.WriteLine($"[Discord] 🎵 Creating audio-specific presence for {snap.AudioSource}");
            
            // Handle different audio sources with timestamps
            return snap.AudioSource switch
            {
                AudioSourceType.YouTubeMusic => CreateYouTubeMusicPresenceWithTimestamps(snap),
                AudioSourceType.Spotify => CreateSpotifyPresenceWithTimestamps(snap),
                _ => CreateGenericAudioPresenceWithTimestamps(snap)
            };
        }

        private RichPresence CreateVideoPresence(MediaSnapshot snap)
        {
            DebugLogger.WriteLine($"[Discord] 📺 Creating video-specific presence for {snap.VideoSource}");
            
            // TODO: Implement platform-specific video presence formats
            // This is where we'll customize the display for each video platform
            return snap.VideoSource switch
            {
                VideoSourceType.YouTube => CreateYouTubePresenceWithTimestamps(snap),
                VideoSourceType.Netflix => CreateNetflixPresenceWithTimestamps(snap),
                VideoSourceType.Twitch => CreateTwitchPresenceWithTimestamps(snap),
                VideoSourceType.PrimeVideo => CreatePrimeVideoPresenceWithTimestamps(snap),
                VideoSourceType.Hulu => CreateHuluPresenceWithTimestamps(snap),
                VideoSourceType.Disney => CreateDisneyPresenceWithTimestamps(snap),
                VideoSourceType.LocalVideo => CreateLocalVideoPresenceWithTimestamps(snap),
                VideoSourceType.Generic => CreateGenericVideoPresenceWithTimestamps(snap),
                _ => CreateGenericVideoPresenceWithTimestamps(snap)
            };
        }

        // Enhanced methods with timestamp support
        private RichPresence CreateYouTubePresenceWithTimestamps(MediaSnapshot snap)
        {
            DebugLogger.WriteLine("[Discord] 📺🔴 Creating YouTube Video-specific presence with timestamps");
            
            var cleanedTitle = CleanTitle(snap.Title);
            
            // For YouTube videos, put the channel name (Artist) as specified - NO service tagging
            string state;
            if (!string.IsNullOrEmpty(snap.Artist))
            {
                var channelName = Truncate(snap.Artist, 50);
                state = $"{channelName} - YouTube";
            }
            else if (!string.IsNullOrEmpty(snap.Album))
            {
                // Fallback: Use Album field as channel name if Artist is not available
                var channelName = Truncate(snap.Album, 50);
                state = $"{channelName} - YouTube";
            }
            else
            {
                state = "YouTube";
            }

            DebugLogger.WriteLine($"[Discord] 📺🔴 YouTube Video presence - Title: '{cleanedTitle}', State: '{state}'");

            return new RichPresence
            {
                Details = Truncate(cleanedTitle, 128),
                State = Truncate(state, 128),
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateYouTubeMusicPresenceWithTimestamps(MediaSnapshot snap)
        {
            DebugLogger.WriteLine("[Discord] 🎵🔴 Creating YouTube Music-specific presence with timestamps");
            
            var details = Truncate(snap.Title, 128);
            var state = !string.IsNullOrEmpty(snap.Artist) ? Truncate(snap.Artist, 128) : "Unknown Artist";
            
            // For YouTube Music, continue as usual - NO service tagging
            if (!string.IsNullOrEmpty(snap.Album))
            {
                state += $" • {Truncate(snap.Album, 50)}";
                state = Truncate(state, 128);
            }
            else
            {
                // Add YouTube Music identifier if no album/channel info
                state += " • YouTube Music";
                state = Truncate(state, 128);
            }

            DebugLogger.WriteLine($"[Discord] 🎵🔴 YouTube Music presence - Title: '{details}', Artist: '{snap.Artist}', Channel: '{snap.Album}'");

            return new RichPresence
            {
                Details = details,
                State = state,
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateSpotifyPresenceWithTimestamps(MediaSnapshot snap)
        {
            DebugLogger.WriteLine("[Discord] 🎵🟢 Creating Spotify-specific presence with timestamps");
            
            var details = Truncate(snap.Title, 128);
            var state = !string.IsNullOrEmpty(snap.Artist) ? Truncate(snap.Artist, 128) : "Unknown Artist";
            var serviceTag = GetServiceDisplayString(snap);
            
            // For Spotify, add album info if available, then service tag
            if (!string.IsNullOrEmpty(snap.Album))
            {
                state += $" • {Truncate(snap.Album, 50)}";
            }
            
            state += serviceTag;
            state = Truncate(state, 128);

            return new RichPresence
            {
                Details = details,
                State = state,
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateGenericVideoPresenceWithTimestamps(MediaSnapshot snap)
        {
            var cleanedTitle = CleanTitle(snap.Title);
            var browserName = GetBrowserName(snap.SourceApp);
            var serviceTag = GetServiceDisplayString(snap);
            
            string state;
            if (!string.IsNullOrEmpty(snap.Artist))
            {
                // Show artist/channel with browser info
                state = $"by {Truncate(snap.Artist, 60)} • Video on {browserName}{serviceTag}";
            }
            else
            {
                state = $"Video on {browserName}{serviceTag}";
            }

            DebugLogger.WriteLine($"[Discord] 📺🌐 Creating generic video presence - Title: '{cleanedTitle}', State: '{state}'");

            return new RichPresence
            {
                Details = Truncate(cleanedTitle, 128),
                State = Truncate(state, 128),
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateGenericAudioPresenceWithTimestamps(MediaSnapshot snap)
        {
            DebugLogger.WriteLine("[Discord] 🎵 Creating generic audio presence with timestamps");
            
            var details = Truncate(snap.Title, 128);
            var state = !string.IsNullOrEmpty(snap.Artist) ? Truncate(snap.Artist, 128) : "Unknown Artist";
            var serviceTag = GetServiceDisplayString(snap);
            
            // For audio, we can add album info if available, then service tag
            if (!string.IsNullOrEmpty(snap.Album))
            {
                state += $" • {Truncate(snap.Album, 50)}";
            }
            
            state += serviceTag;
            state = Truncate(state, 128);

            return new RichPresence
            {
                Details = details,
                State = state,
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateNetflixPresenceWithTimestamps(MediaSnapshot snap)
        {
            var cleanedTitle = CleanTitle(snap.Title);
            var serviceTag = GetServiceDisplayString(snap);
            var state = $"Netflix on {GetBrowserName(snap.SourceApp)}{serviceTag}";
            
            DebugLogger.WriteLine($"[Discord] 📺🎬 Creating Netflix presence - Title: '{cleanedTitle}', State: '{state}'");
            
            return new RichPresence
            {
                Details = Truncate(cleanedTitle, 128),
                State = Truncate(state, 128),
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateTwitchPresenceWithTimestamps(MediaSnapshot snap)
        {
            var cleanedTitle = CleanTitle(snap.Title);
            var serviceTag = GetServiceDisplayString(snap);
            
            string state;
            if (!string.IsNullOrEmpty(snap.Artist))
            {
                // If we have streamer name, show it with the service info
                state = $"watching {Truncate(snap.Artist, 60)} on Twitch{serviceTag}";
            }
            else
            {
                state = $"Twitch on {GetBrowserName(snap.SourceApp)}{serviceTag}";
            }

            DebugLogger.WriteLine($"[Discord] 📺💜 Creating Twitch presence - Title: '{cleanedTitle}', State: '{state}'");

            return new RichPresence
            {
                Details = Truncate(cleanedTitle, 128),
                State = Truncate(state, 128),
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreatePrimeVideoPresenceWithTimestamps(MediaSnapshot snap)
        {
            var browserName = GetBrowserName(snap.SourceApp);
            var cleanedTitle = CleanTitle(snap.Title);
            var albumInfo = $"Prime Video on {browserName}";
            
            DebugLogger.WriteLine($"[Discord] 📺📦 Creating Prime Video presence - Title: '{cleanedTitle}', Album: '{albumInfo}'");
            
            return new RichPresence
            {
                Details = Truncate(cleanedTitle, 128),
                State = Truncate(albumInfo, 128),
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateHuluPresenceWithTimestamps(MediaSnapshot snap)
        {
            var cleanedTitle = CleanTitle(snap.Title);
            var serviceTag = GetServiceDisplayString(snap);
            var state = $"Hulu on {GetBrowserName(snap.SourceApp)}{serviceTag}";
            
            DebugLogger.WriteLine($"[Discord] 📺🟢 Creating Hulu presence - Title: '{cleanedTitle}', State: '{state}'");
            
            return new RichPresence
            {
                Details = Truncate(cleanedTitle, 128),
                State = Truncate(state, 128),
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateDisneyPresenceWithTimestamps(MediaSnapshot snap)
        {
            var cleanedTitle = CleanTitle(snap.Title);
            var serviceTag = GetServiceDisplayString(snap);
            var state = $"Disney+ on {GetBrowserName(snap.SourceApp)}{serviceTag}";
            
            DebugLogger.WriteLine($"[Discord] 📺🏰 Creating Disney+ presence - Title: '{cleanedTitle}', State: '{state}'");
            
            return new RichPresence
            {
                Details = Truncate(cleanedTitle, 128),
                State = Truncate(state, 128),
                Timestamps = CreateTimestamps(snap)
            };
        }

        private RichPresence CreateLocalVideoPresenceWithTimestamps(MediaSnapshot snap)
        {
            return new RichPresence
            {
                Details = Truncate(snap.Title, 128),
                State = GetVideoSourceDescription(snap.SourceApp),
                Timestamps = CreateTimestamps(snap)
            };
        }

        private Timestamps? CreateTimestamps(MediaSnapshot snap)
        {
            try
            {
                // Only show timestamps for playing content
                if (snap.Status != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    DebugLogger.WriteLine("[Discord] ⏸️ Content not playing - no timestamps");
                    return null;
                }

                var total = snap.EndTime - snap.StartTime;
                var elapsed = snap.Position;

                // Enhanced logging for Amazon Prime Video debugging
                bool isPrimeVideo = snap.VideoSource == VideoSourceType.PrimeVideo;
                if (isPrimeVideo)
                {
                    DebugLogger.WriteLine($"[Discord] 📺📦 PRIME VIDEO TIMESTAMP ANALYSIS:");
                    DebugLogger.WriteLine($"[Discord]   StartTime: {snap.StartTime} ({snap.StartTime:mm\\:ss})");
                    DebugLogger.WriteLine($"[Discord]   EndTime: {snap.EndTime} ({snap.EndTime:mm\\:ss})");
                    DebugLogger.WriteLine($"[Discord]   Position: {snap.Position} ({snap.Position:mm\\:ss})");
                    DebugLogger.WriteLine($"[Discord]   Calculated Total: {total} ({total:mm\\:ss})");
                    DebugLogger.WriteLine($"[Discord]   Status: {snap.Status}");
                }

                // Relaxed validation for Prime Video - often has unusual timeline data
                if (isPrimeVideo)
                {
                    // For Prime Video, accept if we have any reasonable position data
                    if (elapsed >= TimeSpan.Zero && elapsed <= TimeSpan.FromHours(12)) // Max 12 hours reasonable
                    {
                        DebugLogger.WriteLine($"[Discord] 📺📦 Prime Video: Using position-only timestamp (Position: {elapsed:mm\\:ss})");
                        
                        // Use elapsed time for "elapsed since start" display
                        var primeNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        var primeStartTime = primeNow - (long)elapsed.TotalSeconds;
                        
                        // If we have a valid total duration, use it
                        if (total > TimeSpan.Zero && total <= TimeSpan.FromHours(12))
                        {
                            var primeEndTime = primeStartTime + (long)total.TotalSeconds;
                            DebugLogger.WriteLine($"[Discord] 📺📦 Prime Video: Full timestamps - Start: {DateTimeOffset.FromUnixTimeSeconds(primeStartTime):HH:mm:ss}, End: {DateTimeOffset.FromUnixTimeSeconds(primeEndTime):HH:mm:ss}");
                            
                            return new Timestamps
                            {
                                StartUnixMilliseconds = (ulong)(primeStartTime * 1000),
                                EndUnixMilliseconds = (ulong)(primeEndTime * 1000)
                            };
                        }
                        else
                        {
                            // Only start time (shows "elapsed" time)
                            DebugLogger.WriteLine($"[Discord] 📺📦 Prime Video: Start-only timestamp - Start: {DateTimeOffset.FromUnixTimeSeconds(primeStartTime):HH:mm:ss}");
                            
                            return new Timestamps
                            {
                                StartUnixMilliseconds = (ulong)(primeStartTime * 1000)
                                // No EndUnixMilliseconds - shows elapsed time only
                            };
                        }
                    }
                    else
                    {
                        DebugLogger.WriteLine($"[Discord] 📺📦 Prime Video: Invalid position data ({elapsed}) - no timestamps");
                        return null;
                    }
                }
                
                // Standard validation for other services
                if (total <= TimeSpan.Zero || elapsed < TimeSpan.Zero)
                {
                    DebugLogger.WriteLine($"[Discord] ⚠️ Invalid duration/position - Total: {total:mm\\:ss}, Position: {elapsed:mm\\:ss} - no timestamps");
                    return null;
                }

                // Calculate when the media started based on current position
                // This ensures the Discord timestamp is synced to the actual current track time
                var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                var startTime = now - (long)elapsed.TotalSeconds;
                var endTime = startTime + (long)total.TotalSeconds;

                DebugLogger.WriteLine($"[Discord] ⏰ Syncing timestamps to current track time:");
                DebugLogger.WriteLine($"[Discord] ⏰   Current Position: {elapsed:mm\\:ss}");
                DebugLogger.WriteLine($"[Discord] ⏰   Total Duration: {total:mm\\:ss}");
                DebugLogger.WriteLine($"[Discord] ⏰   Calculated Start: {DateTimeOffset.FromUnixTimeSeconds(startTime):HH:mm:ss}");
                DebugLogger.WriteLine($"[Discord] ⏰   Calculated End: {DateTimeOffset.FromUnixTimeSeconds(endTime):HH:mm:ss}");
                DebugLogger.WriteLine($"[Discord] ⏰   Unix Start: {startTime}, Unix End: {endTime}");

                return new Timestamps
                {
                    StartUnixMilliseconds = (ulong)(startTime * 1000),
                    EndUnixMilliseconds = (ulong)(endTime * 1000)
                };
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Discord] ❌ Error creating timestamps: {ex.Message}");
                return null;
            }
        }

        private string GetVideoSourceDescription(string sourceApp)
        {
            var appLower = sourceApp.ToLowerInvariant();
            
            return appLower switch
            {
                var app when app.Contains("edge") || app.Contains("chrome") || app.Contains("firefox") => "Watching in browser",
                var app when app.Contains("netflix") => "Watching on Netflix",
                var app when app.Contains("youtube") => "Watching on YouTube", 
                var app when app.Contains("twitch") => "Watching on Twitch",
                var app when app.Contains("discord") => "Watching in Discord",
                var app when app.Contains("vlc") => "Watching in VLC",
                var app when app.Contains("mediaplayer") || app.Contains("films") || app.Contains("tv") => "Watching video",
                _ => "Watching video"
            };
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
            string.IsNullOrEmpty(s) ? s : (s.Length <= max ? $"{s}" : s.Substring(0, max));

        private static string CleanTitle(string title)
        {
            if (string.IsNullOrEmpty(title))
                return title;

            var titleLower = title.ToLowerInvariant();
            
            // Prime Video patterns with their exact lengths for optimization
            // Order matters: check more specific patterns first
            
            // "Watch " prefix - check from start, length = 6
            if (titleLower.StartsWith("watch "))
            {
                var cleanedTitle = title.Substring(6).Trim();
                DebugLogger.WriteLine($"[Discord] 🧹 Cleaned Prime Video 'Watch ' prefix: '{title}' → '{cleanedTitle}'");
                return cleanedTitle;
            }

            // "Watch with" patterns - check for presence and remove everything from there
            var watchWithPatterns = new[]
            {
                ("watch with ", 11),    // Leading pattern
                (" watch with", 11),    // Mid pattern  
                (" - watch with", 13),  // With separator
                (" | watch with", 13)   // With pipe separator
            };

            foreach (var (pattern, length) in watchWithPatterns)
            {
                var index = titleLower.IndexOf(pattern);
                if (index >= 0)
                {
                    // Remove everything from the "watch with" pattern onwards
                    var cleanedTitle = title.Substring(0, index).Trim();
                    DebugLogger.WriteLine($"[Discord] 🧹 Cleaned Prime Video 'Watch with' pattern: '{title}' → '{cleanedTitle}'");
                    return cleanedTitle;
                }
            }

            // Service suffixes - check from end, using fixed lengths
            var serviceSuffixes = new[]
            {
                (" | prime video", 14),  // Length = 14
                (" - prime video", 14)   // Length = 14  
            };

            foreach (var (suffix, length) in serviceSuffixes)
            {
                if (titleLower.EndsWith(suffix))
                {
                    var cleanedTitle = title.Substring(0, title.Length - length).Trim();
                    DebugLogger.WriteLine($"[Discord] 🧹 Cleaned Prime Video service suffix: '{title}' → '{cleanedTitle}'");
                    return cleanedTitle;
                }
            }

            // No patterns matched, return original title
            return title;
        }

        private string GetServiceDisplayString(MediaSnapshot snap)
        {
            // Special handling based on browser-detected service and user requirements
            if (!string.IsNullOrEmpty(snap.BrowserService) && !string.IsNullOrEmpty(snap.BrowserType))
            {
                var service = snap.BrowserService;
                var browserType = snap.BrowserType;
                
                return service switch
                {
                    // YouTube videos show channel name as intended, no service tagging
                    "YouTube" when snap.Type == MediaType.Video => "",
                    
                    // YouTube Music continues as usual, no service tagging
                    "YouTube Music" => "",
                    
                    // Prime Video uses existing cleaning method, no service tagging  
                    "Prime Video" => "",
                    
                    // All other services get "- Service on Browser" tagging
                    "Netflix" => $" - Netflix on {browserType}",
                    "Spotify" => $" - Spotify on {browserType}",
                    "Twitch" => $" - Twitch on {browserType}",
                    "Hulu" => $" - Hulu on {browserType}",
                    "Disney+" => $" - Disney+ on {browserType}",
                    
                    _ => ""
                };
            }
            
            return "";
        }

        private string GetBrowserName(string sourceApp)
        {
            var appLower = sourceApp.ToLowerInvariant();
            
            return appLower switch
            {
                var app when app.Contains("microsoftedge") || app.Contains("msedge") => "Edge",
                var app when app.Contains("chrome") => "Chrome",
                var app when app.Contains("firefox") => "Firefox",
                var app when app.Contains("opera") => "Opera",
                var app when app.Contains("brave") => "Brave",
                var app when app.Contains("safari") => "Safari",
                _ => "Browser"
            };
        }
    }
}
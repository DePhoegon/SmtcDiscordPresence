using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using SmtcDiscordPresence.Services;

namespace SmtcDiscordPresence.Services
{
    public enum MediaType
    {
        Unknown,
        Audio,
        Video
    }

    public enum VideoSourceType
    {
        Unknown,
        YouTube,
        Netflix,
        Twitch,
        PrimeVideo,
        Hulu,
        Disney,
        Generic,
        LocalVideo
    }

    public enum AudioSourceType
    {
        Unknown,
        YouTubeMusic,
        Spotify,
        Generic,
        LocalAudio
    }

    public record MediaSnapshot(
        string Title,
        string Artist,
        string? Album,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus Status,
        TimeSpan Position,
        TimeSpan StartTime,
        TimeSpan EndTime,
        DateTimeOffset LastUpdated,
        MediaType Type,
        string SourceApp,
        VideoSourceType VideoSource,
        AudioSourceType AudioSource,
        string? WebsiteUrl,
        string? BrowserService = null,
        string? BrowserType = null);

    public sealed class MediaSessionService
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _current;
        private BrowserService? _browserService;

        public event EventHandler? SnapshotChanged;

        public async Task InitializeAsync()
        {
            DebugLogger.WriteLine("[MediaSession] Initializing media session service...");
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += async (_, __) => await UpdateCurrentSessionAsync();
            _manager.SessionsChanged += async (_, __) => await UpdateCurrentSessionAsync();
            
            // Initialize unified browser service
            _browserService = new BrowserService();
            DebugLogger.WriteLine("[MediaSession] Browser service initialized.");
            
            await UpdateCurrentSessionAsync();
            DebugLogger.WriteLine("[MediaSession] Media session service initialized successfully.");
        }

        public async Task<MediaSnapshot?> GetSnapshotAsync()
        {
            if (_current == null) 
            {
                DebugLogger.WriteLine("[MediaSession] No current media session available.");
                return null;
            }

            try
            {
                var sourceApp = _current.SourceAppUserModelId ?? "Unknown";
                var props = await _current.TryGetMediaPropertiesAsync();
                var playback = _current.GetPlaybackInfo();
                var timeline = _current.GetTimelineProperties();

                DebugLogger.WriteLine($"[MediaSession] ==================== COMPLETE MEDIA API METADATA DUMP ====================");
                await LogAllAvailableMetadata(_current, props, playback, timeline, sourceApp);
                DebugLogger.WriteLine($"[MediaSession] ============================================================================\n");

                // Check if this is a browser and attempt service detection only
                BrowserInfo? browserInfo = null;
                
                if (IsBrowserApp(sourceApp))
                {
                    DebugLogger.WriteLine($"[MediaSession] 🌐 Browser detected - attempting service detection...");
                    DebugLogger.WriteLine($"[MediaSession] Source App: '{sourceApp}'");
                    
                    // Special handling for MS Edge
                    if (IsEdgeBrowser(sourceApp))
                    {
                        DebugLogger.WriteLine($"[MediaSession] 🔷 MS Edge specifically detected - ensuring proper methodology engagement");
                    }
                    
                    // Use browser service only for service and media type detection
                    browserInfo = await _browserService?.GetCurrentBrowserInfoAsync(sourceApp);
                    
                    if (browserInfo != null)
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ Successfully detected {browserInfo.BrowserType} service information!");
                        DebugLogger.WriteLine($"[MediaSession] Window Title: '{browserInfo.WindowTitle}'");
                        DebugLogger.WriteLine($"[MediaSession] Page Title: '{browserInfo.PageTitle}'");
                        await LogBrowserIntegration(browserInfo, props);
                    }
                    else
                    {
                        DebugLogger.WriteLine($"[MediaSession] ❌ Failed to detect browser service, using fallback service detection");
                        
                        // Special Edge fallback - try to use EdgeBrowserService if BrowserService failed
                        if (IsEdgeBrowser(sourceApp))
                        {
                            DebugLogger.WriteLine($"[MediaSession] 🔷 MS Edge fallback - attempting enhanced Edge detection");
                            
                            // Create a temporary EdgeBrowserService for fallback
                            var tempEdgeService = new EdgeBrowserService();
                            var edgeInfo = await tempEdgeService.GetCurrentEdgeInfoAsync();
                            
                            if (edgeInfo != null)
                            {
                                DebugLogger.WriteLine($"[MediaSession] ✅ Edge fallback successful! Converting EdgeBrowserInfo to BrowserInfo");
                                
                                // Convert EdgeBrowserInfo to BrowserInfo for consistency
                                browserInfo = new BrowserInfo
                                {
                                    WindowTitle = edgeInfo.WindowTitle,
                                    Url = edgeInfo.Url,
                                    PageTitle = edgeInfo.PageTitle,
                                    ProcessId = edgeInfo.ProcessId,
                                    WindowHandle = edgeInfo.WindowHandle,
                                    IsAudioPlaying = edgeInfo.IsAudioPlaying,
                                    BrowserType = "Edge",
                                    ExtractedMetadata = new Dictionary<string, string>(edgeInfo.ExtractedMetadata)
                                };
                                
                                DebugLogger.WriteLine($"[MediaSession] 🔷 Edge fallback conversion complete - continuing with unified flow");
                                await LogBrowserIntegration(browserInfo, props);
                            }
                            else
                            {
                                DebugLogger.WriteLine($"[MediaSession] ❌ Edge fallback also failed - no Edge windows with media found");
                            }
                        }
                    }
                }
                else
                {
                    DebugLogger.WriteLine($"[MediaSession] 🚫 Not a browser app - source: '{sourceApp}'");
                }

                // Enhanced album detection logging
                if (!string.IsNullOrEmpty(props.AlbumTitle) || !string.IsNullOrEmpty(props.AlbumArtist))
                {
                    DebugLogger.WriteLine($"[MediaSession] ✅ ALBUM METADATA DETECTED:");
                    if (!string.IsNullOrEmpty(props.AlbumTitle))
                    {
                        DebugLogger.WriteLine($"[MediaSession]   📀 Album Title: '{props.AlbumTitle}'");
                    }
                    if (!string.IsNullOrEmpty(props.AlbumArtist))
                    {
                        DebugLogger.WriteLine($"[MediaSession]   👤 Album Artist: '{props.AlbumArtist}'");
                    }
                }
                else
                {
                    DebugLogger.WriteLine($"[MediaSession] ❌ NO ALBUM METADATA - Both AlbumTitle and AlbumArtist are empty/null");
                }

                // Analyze metadata for music vs video detection
                DebugLogger.WriteLine($"[MediaSession] ============== MUSIC VS VIDEO ANALYSIS ==============");
                var musicAnalysis = AnalyzeMusicMetadata(props);
                DebugLogger.WriteLine($"[MediaSession] {musicAnalysis.LogMessage}");

                // Use Media API data directly - NO ENHANCEMENT from browser
                var title = props.Title ?? "";
                var artist = props.Artist ?? "";

                // Clean up multi-artist format from Media API
                if (artist.Contains(';') || artist.Contains(','))
                {
                    artist = string.Join(", ",
                        artist.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim()));
                }

                // Enhanced detection using browser info when available
                var videoSource = DetermineVideoSource(sourceApp, props, browserInfo);
                var audioSource = DetermineAudioSource(sourceApp, props, musicAnalysis.IsSuspectedMusic, browserInfo);
                var mediaType = DetermineMediaType(videoSource, audioSource, props, musicAnalysis.IsSuspectedMusic, sourceApp, browserInfo);

                // Extract browser service and type information ONLY for service detection
                string? browserService = null;
                string? browserType = null;
                if (browserInfo != null)
                {
                    browserService = browserInfo.ExtractedMetadata.GetValueOrDefault("Service", null);
                    browserType = browserInfo.BrowserType;
                    
                    DebugLogger.WriteLine($"[MediaSession] 🌐 Browser service detection: Service='{browserService}', Type='{browserType}'");
                    DebugLogger.WriteLine($"[MediaSession] 📊 Using Media API data directly - Title: '{title}', Artist: '{artist}'");
                }

                var snapshot = new MediaSnapshot(
                    Title: title,               // Direct from Media API
                    Artist: artist,             // Direct from Media API (cleaned)
                    Album: props.AlbumTitle,    // Direct from Media API
                    Status: playback.PlaybackStatus,
                    Position: timeline.Position,
                    StartTime: timeline.StartTime,
                    EndTime: timeline.EndTime,
                    LastUpdated: timeline.LastUpdatedTime,
                    Type: mediaType,
                    SourceApp: sourceApp,
                    VideoSource: videoSource,
                    AudioSource: audioSource,
                    WebsiteUrl: null,
                    BrowserService: browserService,    // Only for service detection
                    BrowserType: browserType            // Only for service detection
                );

                DebugLogger.WriteLine($"[MediaSession] ================== FINAL RESULT ==================");
                DebugLogger.WriteLine($"[MediaSession] Final: {mediaType}/{videoSource}/{audioSource} - '{title}'");
                DebugLogger.WriteLine($"[MediaSession] Final Artist: '{snapshot.Artist}' (from Media API)");
                DebugLogger.WriteLine($"[MediaSession] Final Album: '{snapshot.Album}' (from Media API)");
                DebugLogger.WriteLine($"[MediaSession] Current Position: {timeline.Position:mm\\:ss} / {timeline.EndTime - timeline.StartTime:mm\\:ss}");
                
                if (browserInfo != null)
                {
                    var service = browserInfo.ExtractedMetadata.GetValueOrDefault("Service", "Unknown");
                    var detectedMediaType = browserInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "Unknown");
                    
                    var hasService = !string.IsNullOrEmpty(service) && service != "Unknown" && service != "(not detected)";
                    var hasMediaType = !string.IsNullOrEmpty(detectedMediaType) && detectedMediaType != "Unknown" && detectedMediaType != "(not detected)";
                    
                    DebugLogger.WriteLine($"[MediaSession] 🌐 {browserInfo.BrowserType} Authority Status:");
                    DebugLogger.WriteLine($"[MediaSession]   Service: '{service}' {(hasService ? "⚡ AUTHORITY USED" : "❌ NO AUTHORITY")}");
                    DebugLogger.WriteLine($"[MediaSession]   Media Type: '{detectedMediaType}' {(hasMediaType ? "⚡ AUTHORITY USED" : "❌ FALLBACK USED")}");
                    
                    if (hasService && hasMediaType)
                    {
                        DebugLogger.WriteLine($"[MediaSession] 🎯 Browser had complete authority - no fallback logic used");
                    }
                    else
                    {
                        DebugLogger.WriteLine($"[MediaSession] ⚠️ Browser authority incomplete - some fallback logic used");
                    }
                }
                
                DebugLogger.WriteLine($"[MediaSession] 📊 Data Sources: Media API (content) + Browser (service authority)");
                DebugLogger.WriteLine($"[MediaSession] ====================================================");
                return snapshot;
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[MediaSession] Error getting media snapshot: {ex.Message}");
                return null;
            }
        }

        private async Task UpdateCurrentSessionAsync()
        {
            if (_manager == null) return;

            try
            {
                var sessions = _manager.GetSessions();
                
                // Select playing session first, then fallback to any session
                foreach (var session in sessions)
                {
                    var playback = session.GetPlaybackInfo();
                    if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        _current = session;
                        break;
                    }
                }

                if (_current == null)
                {
                    _current = sessions.FirstOrDefault() ?? _manager.GetCurrentSession();
                }

                SnapshotChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[MediaSession] Error updating current session: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Analyze metadata to determine if content is suspected music or video
        /// Music typically has: Album, TrackNumber, structured Artist/Title, PlaybackType.Music
        /// Video typically has: No album, no track number, title might contain video indicators
        /// </summary>
        private (bool IsSuspectedMusic, string LogMessage) AnalyzeMusicMetadata(GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            var musicIndicators = new List<string>();
            var videoIndicators = new List<string>();
            var musicScore = 0;
            var videoScore = 0;

            DebugLogger.WriteLine($"[MediaSession] 🎵🎬 DETAILED METADATA ANALYSIS:");

            // Check for album info (strong music indicator)
            if (!string.IsNullOrEmpty(props.AlbumTitle))
            {
                DebugLogger.WriteLine($"[MediaSession] ✅ ALBUM FOUND: '{props.AlbumTitle}' (+3 music points)");
                musicIndicators.Add($"Has Album: '{props.AlbumTitle}'");
                musicScore += 3;
            }
            else
            {
                DebugLogger.WriteLine($"[MediaSession] ❌ No Album detected (+2 video points)");
                videoIndicators.Add("No Album");
                videoScore += 2;
            }

            // Check for track number (strong music indicator)
            if (props.TrackNumber > 0)
            {
                DebugLogger.WriteLine($"[MediaSession] ✅ TRACK NUMBER FOUND: {props.TrackNumber} (+3 music points)");
                musicIndicators.Add($"Has TrackNumber: {props.TrackNumber}");
                musicScore += 3;
            }
            else
            {
                DebugLogger.WriteLine($"[MediaSession] ❌ No Track Number ({props.TrackNumber}) (+1 video point)");
                videoIndicators.Add("No TrackNumber");
                videoScore += 1;
            }

            // Check PlaybackType if available
            try
            {
                if (props.PlaybackType != null)
                {
                    DebugLogger.WriteLine($"[MediaSession] 🔍 PlaybackType available: {props.PlaybackType.Value}");
                    switch (props.PlaybackType.Value)
                    {
                        case Windows.Media.MediaPlaybackType.Music:
                            DebugLogger.WriteLine($"[MediaSession] ✅ PLAYBACK TYPE: Music (+4 music points)");
                            musicIndicators.Add("PlaybackType: Music");
                            musicScore += 4;
                            break;
                        case Windows.Media.MediaPlaybackType.Video:
                            DebugLogger.WriteLine($"[MediaSession] ✅ PLAYBACK TYPE: Video (+4 video points)");
                            videoIndicators.Add("PlaybackType: Video");
                            videoScore += 4;
                            break;
                        case Windows.Media.MediaPlaybackType.Image:
                            DebugLogger.WriteLine($"[MediaSession] ✅ PLAYBACK TYPE: Image (+2 video points)");
                            videoIndicators.Add("PlaybackType: Image");
                            videoScore += 2;
                            break;
                    }
                }
                else
                {
                    DebugLogger.WriteLine($"[MediaSession] ⚠️ PlaybackType is null");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[MediaSession] ❌ Error accessing PlaybackType: {ex.Message}");
            }

            // Check for AlbumArtist (music indicator)
            if (!string.IsNullOrEmpty(props.AlbumArtist))
            {
                DebugLogger.WriteLine($"[MediaSession] ✅ ALBUM ARTIST FOUND: '{props.AlbumArtist}' (+2 music points)");
                musicIndicators.Add($"Has AlbumArtist: '{props.AlbumArtist}'");
                musicScore += 2;
            }
            else
            {
                DebugLogger.WriteLine($"[MediaSession] ❌ No Album Artist detected");
            }

            // Check title for video content indicators
            var title = props.Title ?? "";
            var titleLower = title.ToLowerInvariant();
            var videoContentPatterns = new[]
            {
                "let's play", "lets play", "gameplay", "walkthrough", "tutorial",
                "review", "reaction", "unboxing", "vlog", "episode", "part ",
                "playthrough", "stream", "live", "highlight", "clip", "trailer",
                "official video", "music video", "lyric video"
            };

            var foundVideoPattern = false;
            foreach (var pattern in videoContentPatterns)
            {
                if (titleLower.Contains(pattern))
                {
                    DebugLogger.WriteLine($"[MediaSession] ❌ VIDEO PATTERN FOUND in title: '{pattern}' (+1 video point)");
                    videoIndicators.Add($"Title contains video pattern: '{pattern}'");
                    videoScore += 1;
                    foundVideoPattern = true;
                    break; // Only count one pattern to avoid over-scoring
                }
            }
            
            if (!foundVideoPattern)
            {
                DebugLogger.WriteLine($"[MediaSession] ✅ No video patterns found in title");
            }

            // Check for clean music structure (title + artist without overlap)
            if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(props.Artist))
            {
                var artistLower = props.Artist.ToLowerInvariant();
                
                // Clean separation of title and artist is a music indicator
                if (!titleLower.Contains(artistLower) && !artistLower.Contains(titleLower))
                {
                    DebugLogger.WriteLine($"[MediaSession] ✅ CLEAN TITLE/ARTIST SEPARATION detected (+2 music points)");
                    musicIndicators.Add("Clean Title/Artist separation");
                    musicScore += 2;
                }
                else
                {
                    DebugLogger.WriteLine($"[MediaSession] ❌ Title/Artist overlap detected - common in videos (+1 video point)");
                    videoIndicators.Add("Title/Artist overlap (common in videos)");
                    videoScore += 1;
                }
            }
            else
            {
                DebugLogger.WriteLine($"[MediaSession] ⚠️ Missing title or artist for structure analysis");
            }

            // Determine final result
            var isSuspectedMusic = musicScore > videoScore;
            var confidence = Math.Abs(musicScore - videoScore);
            
            DebugLogger.WriteLine($"[MediaSession] 📊 ANALYSIS COMPLETE: Music={musicScore} pts, Video={videoScore} pts, Confidence={confidence}");
            
            var logMessage = isSuspectedMusic ? "Is Suspected Music" : "Is Suspected Video";
            logMessage += $" (Music Score: {musicScore}, Video Score: {videoScore}, Confidence: {confidence})";
            
            if (musicIndicators.Any())
            {
                logMessage += $"\n[MediaSession]   Music Indicators: {string.Join(", ", musicIndicators)}";
            }
            
            if (videoIndicators.Any())
            {
                logMessage += $"\n[MediaSession]   Video Indicators: {string.Join(", ", videoIndicators)}";
            }

            return (isSuspectedMusic, logMessage);
        }

        private async Task LogAllAvailableMetadata(
            GlobalSystemMediaTransportControlsSession session,
            GlobalSystemMediaTransportControlsSessionMediaProperties props,
            GlobalSystemMediaTransportControlsSessionPlaybackInfo playback,
            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline,
            string sourceApp)
        {
            try
            {
                DebugLogger.WriteLine($"[MediaSession] 📱 SESSION INFORMATION:");
                DebugLogger.WriteLine($"[MediaSession]   SourceAppUserModelId: '{session.SourceAppUserModelId}'");
                
                // Try to get session display name if available
                try
                {
                    var sessionProps = session.GetType().GetProperties();
                    foreach (var prop in sessionProps)
                    {
                        try
                        {
                            if (prop.CanRead && !prop.PropertyType.IsClass || prop.PropertyType == typeof(string))
                            {
                                var value = prop.GetValue(session);
                                if (value != null && prop.Name != "SourceAppUserModelId")
                                {
                                    DebugLogger.WriteLine($"[MediaSession]   Session.{prop.Name}: '{value}'");
                                }
                            }
                        }
                        catch
                        {
                            // Ignore inaccessible properties
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteLine($"[MediaSession]   Error enumerating session properties: {ex.Message}");
                }

                DebugLogger.WriteLine($"[MediaSession] 🎵 MEDIA PROPERTIES:");
                DebugLogger.WriteLine($"[MediaSession]   Title: '{props.Title ?? "(null)"}'");
                DebugLogger.WriteLine($"[MediaSession]   Artist: '{props.Artist ?? "(null)"}'");
                DebugLogger.WriteLine($"[MediaSession]   AlbumTitle: '{props.AlbumTitle ?? "(null)"}'");
                DebugLogger.WriteLine($"[MediaSession]   AlbumArtist: '{props.AlbumArtist ?? "(null)"}'");
                DebugLogger.WriteLine($"[MediaSession]   Subtitle: '{props.Subtitle ?? "(null)"}'");
                DebugLogger.WriteLine($"[MediaSession]   TrackNumber: {props.TrackNumber}");
                DebugLogger.WriteLine($"[MediaSession]   Genres: [{string.Join(", ", props.Genres?.Select(g => $"'{g}'") ?? new[] { "(null)" })}]");
                
                // PlaybackType with detailed enum info
                try
                {
                    if (props.PlaybackType.HasValue)
                    {
                        var playbackType = props.PlaybackType.Value;
                        DebugLogger.WriteLine($"[MediaSession]   PlaybackType: {playbackType} (Enum Value: {(int)playbackType})");
                    }
                    else
                    {
                        DebugLogger.WriteLine($"[MediaSession]   PlaybackType: (null)");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteLine($"[MediaSession]   PlaybackType: Error accessing - {ex.Message}");
                }

                // Thumbnail information
                try
                {
                    var thumbnail = props.Thumbnail;
                    if (thumbnail != null)
                    {
                        DebugLogger.WriteLine($"[MediaSession]   Thumbnail: Available (Type: {thumbnail.GetType().Name})");
                        // Note: IRandomAccessStreamReference doesn't expose Size/ContentType directly
                        DebugLogger.WriteLine($"[MediaSession]   Thumbnail Stream Reference: Available");
                    }
                    else
                    {
                        DebugLogger.WriteLine($"[MediaSession]   Thumbnail: (null)");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteLine($"[MediaSession]   Thumbnail: Error accessing - {ex.Message}");
                }

                DebugLogger.WriteLine($"[MediaSession] ▶️ PLAYBACK INFORMATION:");
                DebugLogger.WriteLine($"[MediaSession]   PlaybackStatus: {playback.PlaybackStatus} (Enum: {(int)playback.PlaybackStatus})");
                
                try
                {
                    DebugLogger.WriteLine($"[MediaSession]   PlaybackRate: {playback.PlaybackRate ?? 0.0}");
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteLine($"[MediaSession]   PlaybackRate: Error accessing - {ex.Message}");
                }
                
                try
                {
                    DebugLogger.WriteLine($"[MediaSession]   AutoRepeatMode: {playback.AutoRepeatMode} (Enum: {(int)playback.AutoRepeatMode})");
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteLine($"[MediaSession]   AutoRepeatMode: Error accessing - {ex.Message}");
                }
                
                // Controls information
                try
                {
                    var controls = playback.Controls;
                    DebugLogger.WriteLine($"[MediaSession]   Available Controls:");
                    DebugLogger.WriteLine($"[MediaSession]     IsPlayEnabled: {controls.IsPlayEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsPauseEnabled: {controls.IsPauseEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsStopEnabled: {controls.IsStopEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsRecordEnabled: {controls.IsRecordEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsFastForwardEnabled: {controls.IsFastForwardEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsRewindEnabled: {controls.IsRewindEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsNextEnabled: {controls.IsNextEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsPreviousEnabled: {controls.IsPreviousEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsChannelUpEnabled: {controls.IsChannelUpEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsChannelDownEnabled: {controls.IsChannelDownEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsPlayPauseToggleEnabled: {controls.IsPlayPauseToggleEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsShuffleEnabled: {controls.IsShuffleEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsRepeatEnabled: {controls.IsRepeatEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsPlaybackRateEnabled: {controls.IsPlaybackRateEnabled}");
                    DebugLogger.WriteLine($"[MediaSession]     IsPlaybackPositionEnabled: {controls.IsPlaybackPositionEnabled}");
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteLine($"[MediaSession]   Controls: Error accessing - {ex.Message}");
                }

                DebugLogger.WriteLine($"[MediaSession] ⏱️ TIMELINE PROPERTIES:");
                DebugLogger.WriteLine($"[MediaSession]   Position: {timeline.Position} ({timeline.Position:mm\\:ss})");
                DebugLogger.WriteLine($"[MediaSession]   StartTime: {timeline.StartTime} ({timeline.StartTime:mm\\:ss})");
                DebugLogger.WriteLine($"[MediaSession]   EndTime: {timeline.EndTime} ({timeline.EndTime:mm\\:ss})");
                DebugLogger.WriteLine($"[MediaSession]   Duration: {timeline.EndTime - timeline.StartTime} ({(timeline.EndTime - timeline.StartTime):mm\\:ss})");
                DebugLogger.WriteLine($"[MediaSession]   LastUpdatedTime: {timeline.LastUpdatedTime} ({timeline.LastUpdatedTime:HH:mm:ss.fff})");
                DebugLogger.WriteLine($"[MediaSession]   MinSeekTime: {timeline.MinSeekTime} ({timeline.MinSeekTime:mm\\:ss})");
                DebugLogger.WriteLine($"[MediaSession]   MaxSeekTime: {timeline.MaxSeekTime} ({timeline.MaxSeekTime:mm\\:ss})");

                // Try to access any additional properties using reflection
                DebugLogger.WriteLine($"[MediaSession] 🔍 EXTENDED MEDIA PROPERTIES (Reflection):");
                try
                {
                    var mediaPropsType = props.GetType();
                    var allProperties = mediaPropsType.GetProperties();
                    
                    foreach (var prop in allProperties)
                    {
                        try
                        {
                            if (prop.CanRead)
                            {
                                var value = prop.GetValue(props);
                                var valueStr = value switch
                                {
                                    null => "(null)",
                                    string s => $"'{s}'",
                                    System.Collections.IEnumerable enumerable when value is not string => 
                                        $"[{string.Join(", ", enumerable.Cast<object>().Select(o => $"'{o}'"))}]",
                                    _ => value.ToString()
                                };
                                
                                // Only log if we haven't already logged it above
                                var knownProps = new[] { "Title", "Artist", "AlbumTitle", "AlbumArtist", "Subtitle", "TrackNumber", "Genres", "PlaybackType", "Thumbnail" };
                                if (!knownProps.Contains(prop.Name))
                                {
                                    DebugLogger.WriteLine($"[MediaSession]   Extended.{prop.Name}: {valueStr} (Type: {prop.PropertyType.Name})");
                                }
                            }
                        }
                        catch (Exception propEx)
                        {
                            DebugLogger.WriteLine($"[MediaSession]   Extended.{prop.Name}: Error accessing - {propEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.WriteLine($"[MediaSession]   Extended Properties: Error enumerating - {ex.Message}");
                }

                // Try to access system info
                DebugLogger.WriteLine($"[MediaSession] 💻 SYSTEM INFORMATION:");
                DebugLogger.WriteLine($"[MediaSession]   Windows Media Session Manager Available: {_manager != null}");
                
                if (_manager != null)
                {
                    try
                    {
                        var allSessions = _manager.GetSessions();
                        DebugLogger.WriteLine($"[MediaSession]   Total Active Sessions: {allSessions.Count}");
                        
                        for (int i = 0; i < allSessions.Count; i++)
                        {
                            var sess = allSessions[i];
                            var isCurrentSession = sess == session;
                            DebugLogger.WriteLine($"[MediaSession]   Session {i + 1}: '{sess.SourceAppUserModelId}' {(isCurrentSession ? "⭐ CURRENT" : "")}");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.WriteLine($"[MediaSession]   Session enumeration error: {ex.Message}");
                    }
                }

                DebugLogger.WriteLine($"[MediaSession] 📊 METADATA SUMMARY:");
                var hasTitle = !string.IsNullOrEmpty(props.Title);
                var hasArtist = !string.IsNullOrEmpty(props.Artist);
                var hasAlbum = !string.IsNullOrEmpty(props.AlbumTitle);
                var hasAlbumArtist = !string.IsNullOrEmpty(props.AlbumArtist);
                var hasSubtitle = !string.IsNullOrEmpty(props.Subtitle);
                var hasTrackNumber = props.TrackNumber > 0;
                var hasGenres = props.Genres?.Any() == true;
                var hasPlaybackType = props.PlaybackType.HasValue;
                var hasThumbnail = props.Thumbnail != null;

                DebugLogger.WriteLine($"[MediaSession]   Metadata Completeness:");
                DebugLogger.WriteLine($"[MediaSession]     Title: {(hasTitle ? "YES" : "NO")} | Artist: {(hasArtist ? "YES" : "NO")} | Album: {(hasAlbum ? "YES" : "NO")}");
                DebugLogger.WriteLine($"[MediaSession]     AlbumArtist: {(hasAlbumArtist ? "YES" : "NO")} | Subtitle: {(hasSubtitle ? "YES" : "NO")} | TrackNumber: {(hasTrackNumber ? "YES" : "NO")}");
                DebugLogger.WriteLine($"[MediaSession]     Genres: {(hasGenres ? "YES" : "NO")} | PlaybackType: {(hasPlaybackType ? "YES" : "NO")} | Thumbnail: {(hasThumbnail ? "YES" : "NO")}");
               
                var totalMetadataFields = 9;
                var populatedFields = (new[] { hasTitle, hasArtist, hasAlbum, hasAlbumArtist, hasSubtitle, hasTrackNumber, hasGenres, hasPlaybackType, hasThumbnail }).Count(x => x);
                var completenessPercentage = (populatedFields * 100) / totalMetadataFields;
                
                DebugLogger.WriteLine($"[MediaSession]   Metadata Completeness: {populatedFields}/{totalMetadataFields} fields ({completenessPercentage}%)");

            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[MediaSession] ❌ Error during comprehensive metadata logging: {ex.Message}");
                DebugLogger.WriteLine($"[MediaSession] ❌ Stack trace: {ex.StackTrace}");
            }
        }

        private VideoSourceType DetermineVideoSource(string sourceApp, GlobalSystemMediaTransportControlsSessionMediaProperties props, BrowserInfo? browserInfo = null)
        {
            var appLower = sourceApp.ToLowerInvariant();

            // Check for browser apps
            if (IsBrowserApp(appLower))
            {
                // If we have browser info, use it for service detection - BROWSER IS ABSOLUTE AUTHORITY
                if (browserInfo != null)
                {
                    var service = browserInfo.ExtractedMetadata.GetValueOrDefault("Service", "");
                    var mediaType = browserInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "");
                    
                    DebugLogger.WriteLine($"[MediaSession] 🌐 Using {browserInfo.BrowserType} browser video detection: Service='{service}', MediaType='{mediaType}'");
                    
                    // Only return video sources for Video media type
                    if (mediaType == "Video" && !string.IsNullOrEmpty(service))
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ Browser detected VIDEO service - using direct mapping");
                        return service.ToLowerInvariant() switch
                        {
                            "youtube" => VideoSourceType.YouTube,
                            "netflix" => VideoSourceType.Netflix,
                            "prime video" => VideoSourceType.PrimeVideo,
                            "twitch" => VideoSourceType.Twitch,
                            "hulu" => VideoSourceType.Hulu,
                            "disney+" => VideoSourceType.Disney,
                            _ => VideoSourceType.YouTube // Default for browser video content
                        };
                    }
                    
                    // If browser says it's NOT video (i.e., Audio), don't assign video source
                    if (mediaType == "Audio")
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ Browser detected AUDIO - no video source assigned");
                        return VideoSourceType.Unknown;
                    }
                    
                    // If no media type detected but service is present, use fallback
                    if (!string.IsNullOrEmpty(service) && (string.IsNullOrEmpty(mediaType) || mediaType == "(not detected)"))
                    {
                        DebugLogger.WriteLine($"[MediaSession] ⚠️ Browser service '{service}' detected but no media type - defaulting to YouTube");
                        return VideoSourceType.YouTube;
                    }
                }

                // Fallback to original detection method if browser detection failed
                DebugLogger.WriteLine($"[MediaSession] 🔄 Browser detection failed - using metadata fallback");
                var allText = $"{props.Title} {props.Artist} {props.AlbumTitle} {props.AlbumArtist} {props.Subtitle}";
                
                if (allText.Contains(" | Prime Video") || allText.Contains(" - Prime Video"))
                {
                    return VideoSourceType.PrimeVideo;
                }
                if (allText.Contains(" | Netflix") || allText.Contains(" - Netflix"))
                {
                    return VideoSourceType.Netflix;
                }
                if (allText.Contains(" - Twitch") || allText.Contains(" | Twitch"))
                {
                    return VideoSourceType.Twitch;
                }
                if (allText.Contains(" | Hulu") || allText.Contains(" - Hulu"))
                {
                    return VideoSourceType.Hulu;
                }
                if (allText.Contains(" | Disney+") || allText.Contains(" - Disney+"))
                {
                    return VideoSourceType.Disney;
                }

                // For browser content without specific service identifiers, default to YouTube
                return VideoSourceType.YouTube;
            }

            // Dedicated apps
            if (appLower.Contains("netflix")) return VideoSourceType.Netflix;
            if (appLower.Contains("youtube")) return VideoSourceType.YouTube;
            if (appLower.Contains("twitch")) return VideoSourceType.Twitch;
            if (appLower.Contains("prime") || appLower.Contains("amazon")) return VideoSourceType.PrimeVideo;
            if (appLower.Contains("hulu")) return VideoSourceType.Hulu;
            if (appLower.Contains("disney")) return VideoSourceType.Disney;

            return VideoSourceType.LocalVideo;
        }

        private AudioSourceType DetermineAudioSource(string sourceApp, GlobalSystemMediaTransportControlsSessionMediaProperties props, bool isSuspectedMusic, BrowserInfo? browserInfo = null)
        {
            var appLower = sourceApp.ToLowerInvariant();

            // Check for browser apps
            if (IsBrowserApp(appLower))
            {
                // If we have browser info, use it for service detection - BROWSER IS ABSOLUTE AUTHORITY
                if (browserInfo != null)
                {
                    var service = browserInfo.ExtractedMetadata.GetValueOrDefault("Service", "");
                    var mediaType = browserInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "");
                    
                    DebugLogger.WriteLine($"[MediaSession] 🌐 Using {browserInfo.BrowserType} browser audio detection: Service='{service}', MediaType='{mediaType}'");
                    
                    // Direct service to audio source mapping ONLY for Audio media type
                    if (mediaType == "Audio" && !string.IsNullOrEmpty(service))
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ Browser detected AUDIO service - using direct mapping");
                        return service.ToLowerInvariant() switch
                        {
                            "youtube music" => AudioSourceType.YouTubeMusic,
                            "spotify" => AudioSourceType.Spotify,
                            _ => AudioSourceType.Generic
                        };
                    }
                    
                    // If browser says it's NOT audio (i.e., Video), don't override with suspected music
                    if (mediaType == "Video")
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ Browser detected VIDEO - no audio source assigned");
                        return AudioSourceType.Unknown;
                    }
                    
                    // If no media type detected by browser, fall back to service analysis
                    if (string.IsNullOrEmpty(mediaType) || mediaType == "(not detected)")
                    {
                        DebugLogger.WriteLine($"[MediaSession] ⚠️ Browser service '{service}' detected but no media type - using fallback logic");
                        // Only use suspected music logic if browser couldn't determine media type
                        if (service.ToLowerInvariant() == "youtube" && isSuspectedMusic)
                        {
                            DebugLogger.WriteLine($"[MediaSession] 🎵 YouTube with suspected music (fallback) - treating as YouTube Music");
                            return AudioSourceType.YouTubeMusic;
                        }
                    }
                }

                // Fallback to original detection method if browser detection failed
                DebugLogger.WriteLine($"[MediaSession] 🔄 Browser detection failed - using metadata fallback");
                var allText = $"{props.Title} {props.Artist} {props.AlbumTitle} {props.AlbumArtist}";
                
                if (allText.Contains(" - Spotify") || allText.Contains(" | Spotify"))
                {
                    return AudioSourceType.Spotify;
                }

                // For browser content that's suspected to be music, assume YouTube Music
                if (isSuspectedMusic)
                {
                    return AudioSourceType.YouTubeMusic;
                }

                return AudioSourceType.Generic;
            }

            // Dedicated apps
            if (appLower.Contains("spotify")) return AudioSourceType.Spotify;
            if (appLower.Contains("youtube") && appLower.Contains("music")) return AudioSourceType.YouTubeMusic;

            return AudioSourceType.LocalAudio;
        }

        private MediaType DetermineMediaType(VideoSourceType videoSource, AudioSourceType audioSource, GlobalSystemMediaTransportControlsSessionMediaProperties props, bool isSuspectedMusic, string sourceApp, BrowserInfo? browserInfo = null)
        {
            DebugLogger.WriteLine($"[MediaSession] ============== MEDIA TYPE DETERMINATION ==============");
            DebugLogger.WriteLine($"[MediaSession] Video Source: {videoSource}");
            DebugLogger.WriteLine($"[MediaSession] Audio Source: {audioSource}");
            DebugLogger.WriteLine($"[MediaSession] Is Suspected Music: {isSuspectedMusic}");
            DebugLogger.WriteLine($"[MediaSession] Source App: {sourceApp}");
            
            // BROWSER DETECTION IS ABSOLUTE AUTHORITY - No fallbacks if browser detected service and type
            if (browserInfo != null)
            {
                var browserMediaType = browserInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "");
                var browserService = browserInfo.ExtractedMetadata.GetValueOrDefault("Service", "");
                
                DebugLogger.WriteLine($"[MediaSession] 🌐 Browser detected media type: '{browserMediaType}' for service: '{browserService}'");
                
                if (!string.IsNullOrEmpty(browserMediaType) && browserMediaType != "(not detected)")
                {
                    if (browserMediaType == "Audio")
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ FINAL DECISION: AUDIO - Based on browser detection (ABSOLUTE AUTHORITY)");
                        return MediaType.Audio;
                    }
                    else if (browserMediaType == "Video")
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ FINAL DECISION: VIDEO - Based on browser detection (ABSOLUTE AUTHORITY)");
                        return MediaType.Video;
                    }
                }
                else
                {
                    DebugLogger.WriteLine($"[MediaSession] ⚠️ Browser detection available but MediaType is empty or not detected");
                }
            }
            else
            {
                DebugLogger.WriteLine($"[MediaSession] ⚠️ No browser info available for media type determination");
            }
            
            DebugLogger.WriteLine($"[MediaSession] 🔄 Falling back to legacy detection methods...");
            
            // Only use fallback methods if browser detection failed
            var appLower = sourceApp.ToLowerInvariant();
            if (IsBrowserApp(appLower))
            {
                DebugLogger.WriteLine($"[MediaSession] ⚠️ Browser app detected but browser service failed - using metadata analysis");
                if (isSuspectedMusic)
                {
                    DebugLogger.WriteLine($"[MediaSession] ✅ Determined as AUDIO due to suspected music analysis (fallback)");
                    return MediaType.Audio;
                }
                else
                {
                    DebugLogger.WriteLine($"[MediaSession] ✅ Determined as VIDEO due to suspected video analysis (fallback)");
                    return MediaType.Video;
                }
            }
            
            // If we detected a specific audio source, it's audio
            if (audioSource != AudioSourceType.Unknown && audioSource != AudioSourceType.Generic)
            {
                DebugLogger.WriteLine($"[MediaSession] ✅ Determined as AUDIO due to specific audio source: {audioSource} (fallback)");
                return MediaType.Audio;
            }

            // If we detected a specific video source, it's video
            if (videoSource != VideoSourceType.Unknown && videoSource != VideoSourceType.Generic)
            {
                DebugLogger.WriteLine($"[MediaSession] ✅ Determined as VIDEO due to specific video source: {videoSource} (fallback)");
                return MediaType.Video;
            }

            // Fallback based on metadata
            bool hasArtistAndAlbum = !string.IsNullOrWhiteSpace(props.Artist) && !string.IsNullOrWhiteSpace(props.AlbumTitle);
            DebugLogger.WriteLine($"[MediaSession] 🔍 Metadata fallback check - Has Artist: {!string.IsNullOrWhiteSpace(props.Artist)}, Has Album: {!string.IsNullOrWhiteSpace(props.AlbumTitle)}");
            
            if (hasArtistAndAlbum)
            {
                DebugLogger.WriteLine($"[MediaSession] ✅ Determined as AUDIO due to Artist+Album metadata (final fallback)");
                return MediaType.Audio;
            }

            DebugLogger.WriteLine($"[MediaSession] ⚠️ Defaulting to VIDEO (no strong indicators for audio)");
            return MediaType.Video;
        }

        private bool IsBrowserApp(string sourceApp)
        {
            var appLower = sourceApp.ToLowerInvariant();
            
            // Enhanced Edge detection - check for all possible Edge identifiers
            bool isEdge = appLower.Contains("microsoftedge") || appLower.Contains("msedge") || appLower.Contains("edge");
            bool isChrome = appLower.Contains("chrome");
            bool isFirefox = appLower.Contains("firefox") || appLower.Contains("mozilla");
            bool isOtherBrowser = appLower.Contains("opera") || appLower.Contains("brave") || appLower.Contains("safari");
            
            var isBrowser = isEdge || isChrome || isFirefox || isOtherBrowser;
            
            DebugLogger.WriteLine($"[MediaSession] 🔍 Browser detection for sourceApp: '{sourceApp}'");
            DebugLogger.WriteLine($"[MediaSession]   Is Edge: {isEdge}");
            DebugLogger.WriteLine($"[MediaSession]   Is Chrome: {isChrome}");
            DebugLogger.WriteLine($"[MediaSession]   Is Firefox: {isFirefox}");
            DebugLogger.WriteLine($"[MediaSession]   Is Other Browser: {isOtherBrowser}");
            DebugLogger.WriteLine($"[MediaSession]   Final Result: {isBrowser}");
            
            return isBrowser;
        }

        private bool IsEdgeBrowser(string sourceApp)
        {
            var appLower = sourceApp.ToLowerInvariant();
            return appLower.Contains("microsoftedge") || appLower.Contains("msedge") || appLower.Contains("edge");
        }

        private async Task LogBrowserIntegration(BrowserInfo browserInfo, GlobalSystemMediaTransportControlsSessionMediaProperties props)
        {
            try
            {
                DebugLogger.WriteLine($"[MediaSession] 🌐 ============== {browserInfo.BrowserType.ToUpper()} BROWSER INTEGRATION ==============");
                DebugLogger.WriteLine($"[MediaSession] 🔄 BROWSER DETECTION IS ABSOLUTE AUTHORITY FOR SERVICE & MEDIA TYPE");
                DebugLogger.WriteLine($"[MediaSession] ");
                DebugLogger.WriteLine($"[MediaSession] 📊 MEDIA API DATA (PRIMARY SOURCE FOR CONTENT):");
                DebugLogger.WriteLine($"[MediaSession]   API Title: '{props.Title ?? "(null)"}' ✅ USED FOR TITLE");
                DebugLogger.WriteLine($"[MediaSession]   API Artist: '{props.Artist ?? "(null)"}' ✅ USED FOR ARTIST");
                DebugLogger.WriteLine($"[MediaSession]   API Album: '{props.AlbumTitle ?? "(null)"}' ✅ USED FOR ALBUM");
                DebugLogger.WriteLine($"[MediaSession] ");
                DebugLogger.WriteLine($"[MediaSession] 🌐 {browserInfo.BrowserType.ToUpper()} BROWSER DATA (ABSOLUTE AUTHORITY FOR SERVICE TYPE):");
                
                var service = browserInfo.ExtractedMetadata.GetValueOrDefault("Service", "(not detected)");
                var mediaType = browserInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "(not detected)");
                
                DebugLogger.WriteLine($"[MediaSession]   Detected Service: '{service}' ⚡ ABSOLUTE AUTHORITY");
                DebugLogger.WriteLine($"[MediaSession]   Detected Media Type: '{mediaType}' ⚡ ABSOLUTE AUTHORITY - NO OVERRIDES ALLOWED");
                DebugLogger.WriteLine($"[MediaSession] ");
                
                var hasServiceDetection = !string.IsNullOrEmpty(service) && service != "(not detected)";
                var hasMediaTypeDetection = !string.IsNullOrEmpty(mediaType) && mediaType != "(not detected)";
                
                DebugLogger.WriteLine($"[MediaSession] ⚡ AUTHORITY STATUS:");
                DebugLogger.WriteLine($"[MediaSession]   Service Authority: {(hasServiceDetection ? "✅ ACTIVE" : "❌ INACTIVE")} - '{service}'");
                DebugLogger.WriteLine($"[MediaSession]   Media Type Authority: {(hasMediaTypeDetection ? "✅ ACTIVE" : "❌ INACTIVE")} - '{mediaType}'");
                
                if (hasServiceDetection && hasMediaTypeDetection)
                {
                    DebugLogger.WriteLine($"[MediaSession] 🎯 RESULT: Browser has complete authority over service classification");
                }
                else if (hasServiceDetection)
                {
                    DebugLogger.WriteLine($"[MediaSession] ⚠️ PARTIAL: Browser has service authority but media type will use fallback");
                }
                else
                {
                    DebugLogger.WriteLine($"[MediaSession] ❌ FALLBACK: Browser detection failed, using legacy methods");
                }
                
                DebugLogger.WriteLine($"[MediaSession] 📋 RULE: If browser detects service & type, NO OTHER LOGIC CAN OVERRIDE");
                DebugLogger.WriteLine($"[MediaSession] ========================================================");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[MediaSession] ❌ Error logging browser integration: {ex.Message}");
            }
        }

        /// <summary>
        /// Test method to verify browser integration is working
        /// </summary>
        public async Task<bool> TestBrowserIntegrationAsync()
        {
            try
            {
                DebugLogger.WriteLine("[MediaSession] 🧪 Testing unified browser integration...");
                
                if (_browserService == null)
                {
                    DebugLogger.WriteLine("[MediaSession] ❌ Browser service not initialized");
                    return false;
                }

                // Test with Edge specifically since that was the one failing
                var edgeSourceApps = new[] 
                {
                    "Microsoft.MicrosoftEdge_8wekyb3d8bbwe",
                    "msedge",
                    "MicrosoftEdge"
                };
                
                bool edgeTestSuccessful = false;
                
                foreach (var edgeSourceApp in edgeSourceApps)
                {
                    DebugLogger.WriteLine($"[MediaSession] 🔷 Testing MS Edge browser detection for: '{edgeSourceApp}'");
                    
                    var browserInfo = await _browserService.GetCurrentBrowserInfoAsync(edgeSourceApp);
                    
                    if (browserInfo != null)
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ MS Edge integration successful for {browserInfo.BrowserType}!");
                        DebugLogger.WriteLine($"[MediaSession] Found window: '{browserInfo.WindowTitle}'");
                        
                        // Log the extracted service and media type
                        var service = browserInfo.ExtractedMetadata.GetValueOrDefault("Service", "Unknown");
                        var mediaType = browserInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "Unknown");
                        DebugLogger.WriteLine($"[MediaSession] Service: '{service}', Media Type: '{mediaType}'");
                        
                        edgeTestSuccessful = true;
                        break;
                    }
                    else
                    {
                        DebugLogger.WriteLine($"[MediaSession] ⚠️ No Edge windows found for sourceApp: '{edgeSourceApp}'");
                    }
                }
                
                if (!edgeTestSuccessful)
                {
                    DebugLogger.WriteLine("[MediaSession] 🔷 MS Edge unified service failed - testing EdgeBrowserService fallback");
                    
                    // Test EdgeBrowserService fallback
                    var tempEdgeService = new EdgeBrowserService();
                    var edgeInfo = await tempEdgeService.GetCurrentEdgeInfoAsync();
                    
                    if (edgeInfo != null)
                    {
                        DebugLogger.WriteLine($"[MediaSession] ✅ MS Edge fallback service successful!");
                        DebugLogger.WriteLine($"[MediaSession] Found Edge window: '{edgeInfo.WindowTitle}'");
                        
                        var service = edgeInfo.ExtractedMetadata.GetValueOrDefault("Service", "Unknown");
                        var mediaType = edgeInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "Unknown");
                        DebugLogger.WriteLine($"[MediaSession] Edge Service: '{service}', Media Type: '{mediaType}'");
                        
                        edgeTestSuccessful = true;
                    }
                    else
                    {
                        DebugLogger.WriteLine("[MediaSession] ❌ MS Edge fallback also failed - no Edge windows found with media content");
                    }
                }
                
                if (edgeTestSuccessful)
                {
                    DebugLogger.WriteLine("[MediaSession] 🎯 MS Edge methodology successfully engaged!");
                    return true;
                }
                else
                {
                    DebugLogger.WriteLine("[MediaSession] ⚠️ MS Edge test failed - trying other browsers as fallback");
                    
                    // Try other browser types as fallback
                    var testSources = new[] { "chrome", "firefox" };
                    
                    foreach (var sourceApp in testSources)
                    {
                        DebugLogger.WriteLine($"[MediaSession] 🔍 Testing fallback browser detection for: '{sourceApp}'");
                        var fallbackInfo = await _browserService.GetCurrentBrowserInfoAsync(sourceApp);
                        
                        if (fallbackInfo != null)
                        {
                            DebugLogger.WriteLine($"[MediaSession] ✅ Fallback browser integration successful for {fallbackInfo.BrowserType}!");
                            return true;
                        }
                    }
                    
                    DebugLogger.WriteLine("[MediaSession] ❌ No browser windows found (this is normal if no browsers are running with media)");
                    return false;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[MediaSession] ❌ Browser integration test failed: {ex.Message}");
                DebugLogger.WriteLine($"[MediaSession] ❌ Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Legacy compatibility method - redirects to TestBrowserIntegrationAsync
        /// </summary>
        public async Task<bool> TestEdgeIntegrationAsync()
        {
            DebugLogger.WriteLine("[MediaSession] ⚠️ TestEdgeIntegrationAsync is deprecated - using unified TestBrowserIntegrationAsync instead");
            return await TestBrowserIntegrationAsync();
        }
    }
}
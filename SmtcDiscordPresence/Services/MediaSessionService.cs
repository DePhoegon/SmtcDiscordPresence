using System;
using System.Linq;
using System.Threading.Tasks;
using Windows.Media.Control;
using SmtcDiscordPresence.Services;

namespace SmtcDiscordPresence.Services
{
    public record MediaSnapshot(
        string Title,
        string Artist,
        string? Album,
        GlobalSystemMediaTransportControlsSessionPlaybackStatus Status,
        TimeSpan Position,
        TimeSpan StartTime,
        TimeSpan EndTime,
        DateTimeOffset LastUpdated);

    public sealed class MediaSessionService
    {
        private GlobalSystemMediaTransportControlsSessionManager? _manager;
        private GlobalSystemMediaTransportControlsSession? _current;

        public event EventHandler? SnapshotChanged;

        public async Task InitializeAsync()
        {
            DebugLogger.WriteLine("[MediaSession] Initializing media session service...");
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _manager.CurrentSessionChanged += async (_, __) => await UpdateCurrentSessionAsync();
            _manager.SessionsChanged += async (_, __) => await UpdateCurrentSessionAsync();
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
                DebugLogger.WriteLine($"[MediaSession] Getting snapshot from: {_current.SourceAppUserModelId}");
                
                var props = await _current.TryGetMediaPropertiesAsync();
                var playback = _current.GetPlaybackInfo();
                var timeline = _current.GetTimelineProperties();

                DebugLogger.WriteLine($"[MediaSession] Raw media properties:");
                DebugLogger.WriteLine($"  Title: '{props.Title}'");
                DebugLogger.WriteLine($"  Artist: '{props.Artist}'");
                DebugLogger.WriteLine($"  AlbumTitle: '{props.AlbumTitle}'");
                DebugLogger.WriteLine($"  AlbumArtist: '{props.AlbumArtist}'");
                DebugLogger.WriteLine($"  TrackNumber: {props.TrackNumber}");

                string artist = props.Artist ?? "";
                if (artist.Contains(';') || artist.Contains(','))
                {
                    artist = string.Join(", ",
                        artist.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(s => s.Trim()));
                }

                var snapshot = new MediaSnapshot(
                    Title: props.Title ?? "",
                    Artist: artist,
                    Album: props.AlbumTitle,
                    Status: playback.PlaybackStatus,
                    Position: timeline.Position,
                    StartTime: timeline.StartTime,
                    EndTime: timeline.EndTime,
                    LastUpdated: timeline.LastUpdatedTime
                );

                DebugLogger.WriteLine($"[MediaSession] Media snapshot created:");
                DebugLogger.WriteLine($"  Title: '{snapshot.Title}'");
                DebugLogger.WriteLine($"  Artist: '{snapshot.Artist}'");
                DebugLogger.WriteLine($"  Album: '{snapshot.Album}'");
                DebugLogger.WriteLine($"  Status: {snapshot.Status}");
                DebugLogger.WriteLine($"  Position: {snapshot.Position}");
                DebugLogger.WriteLine($"  Duration: {snapshot.EndTime - snapshot.StartTime}");

                return snapshot;
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[MediaSession] Error getting media snapshot: {ex.Message}");
                DebugLogger.WriteLine($"[MediaSession] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private async Task UpdateCurrentSessionAsync()
        {
            if (_manager == null) 
            {
                DebugLogger.WriteLine("[MediaSession] Manager is null, cannot update session.");
                return;
            }

            try
            {
                var sessions = _manager.GetSessions();
                DebugLogger.WriteLine($"[MediaSession] Found {sessions.Count} media sessions.");

                // Log all sessions with more detail
                for (int i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    var playback = session.GetPlaybackInfo();
                    var appId = session.SourceAppUserModelId ?? "Unknown";
                    
                    DebugLogger.WriteLine($"  Session {i}: Status = {playback.PlaybackStatus}, SourceAppUserModelId = {appId}");
                    
                    // Try to get media properties to see if there's content
                    try
                    {
                        var props = await session.TryGetMediaPropertiesAsync();
                        var hasContent = !string.IsNullOrWhiteSpace(props.Title) || !string.IsNullOrWhiteSpace(props.Artist);
                        DebugLogger.WriteLine($"    Title: '{props.Title}', Artist: '{props.Artist}', HasContent: {hasContent}");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.WriteLine($"    Could not get media properties: {ex.Message}");
                    }
                }

                // Improved session selection logic:
                // 1. First prefer actively playing sessions with content
                // 2. Then any playing session
                // 3. Then paused sessions with content
                // 4. Finally fall back to current session
                
                GlobalSystemMediaTransportControlsSession? selectedSession = null;
                
                // Try to find actively playing session with media content
                foreach (var session in sessions)
                {
                    var playback = session.GetPlaybackInfo();
                    if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    {
                        try
                        {
                            var props = await session.TryGetMediaPropertiesAsync();
                            if (!string.IsNullOrWhiteSpace(props.Title) || !string.IsNullOrWhiteSpace(props.Artist))
                            {
                                selectedSession = session;
                                DebugLogger.WriteLine($"[MediaSession] Selected playing session with content: {session.SourceAppUserModelId}");
                                break;
                            }
                        }
                        catch
                        {
                            // If we can't get properties, still consider it as a candidate
                            selectedSession = session;
                        }
                    }
                }
                
                // If no playing session found, look for paused sessions with content
                if (selectedSession == null)
                {
                    foreach (var session in sessions)
                    {
                        var playback = session.GetPlaybackInfo();
                        if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused)
                        {
                            try
                            {
                                var props = await session.TryGetMediaPropertiesAsync();
                                if (!string.IsNullOrWhiteSpace(props.Title) || !string.IsNullOrWhiteSpace(props.Artist))
                                {
                                    selectedSession = session;
                                    DebugLogger.WriteLine($"[MediaSession] Selected paused session with content: {session.SourceAppUserModelId}");
                                    break;
                                }
                            }
                            catch
                            {
                                // Continue to next session if properties can't be read
                            }
                        }
                    }
                }
                
                // Fall back to any session or current session
                if (selectedSession == null)
                {
                    selectedSession = sessions.FirstOrDefault() ?? _manager.GetCurrentSession();
                    if (selectedSession != null)
                    {
                        DebugLogger.WriteLine($"[MediaSession] Fell back to session: {selectedSession.SourceAppUserModelId}");
                    }
                }

                _current = selectedSession;

                if (_current != null)
                {
                    var playback = _current.GetPlaybackInfo();
                    DebugLogger.WriteLine($"[MediaSession] Final selected session: SourceAppUserModelId = {_current.SourceAppUserModelId}, Status = {playback.PlaybackStatus}");
                }
                else
                {
                    DebugLogger.WriteLine("[MediaSession] No session selected - no media sessions available.");
                }

                SnapshotChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[MediaSession] Error updating current session: {ex.Message}");
            }

            await Task.CompletedTask;
        }
    }
}
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SmtcDiscordPresence.Services
{
    public class EdgeBrowserInfo
    {
        public string? WindowTitle { get; set; }
        public string? Url { get; set; }
        public string? PageTitle { get; set; }
        public int ProcessId { get; set; }
        public IntPtr WindowHandle { get; set; }
        public bool IsAudioPlaying { get; set; }
        public Dictionary<string, string> ExtractedMetadata { get; set; } = new();
    }

    public sealed class EdgeBrowserService
    {
        // Windows API declarations
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder strText, int maxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public async Task<EdgeBrowserInfo?> GetCurrentEdgeInfoAsync()
        {
            try
            {
                DebugLogger.WriteLine("[EdgeBrowser] ?? Starting direct Edge browser information extraction...");
                
                // Get all Edge processes
                var edgeProcesses = GetEdgeProcesses();
                if (!edgeProcesses.Any())
                {
                    DebugLogger.WriteLine("[EdgeBrowser] ? No Edge processes found");
                    return null;
                }

                DebugLogger.WriteLine($"[EdgeBrowser] ?? Found {edgeProcesses.Count} Edge processes");

                // Get Edge windows
                var edgeWindows = await GetEdgeWindowsAsync(edgeProcesses);
                if (!edgeWindows.Any())
                {
                    DebugLogger.WriteLine("[EdgeBrowser] ? No Edge windows found");
                    return null;
                }

                DebugLogger.WriteLine($"[EdgeBrowser] ?? Found {edgeWindows.Count} Edge windows");

                // Find the active/focused Edge window or one with media
                var activeEdgeInfo = await FindActiveMediaWindowAsync(edgeWindows);
                if (activeEdgeInfo != null)
                {
                    DebugLogger.WriteLine($"[EdgeBrowser] ? Found active media window: '{activeEdgeInfo.WindowTitle}'");
                    await EnrichWithAdditionalInfoAsync(activeEdgeInfo);
                    return activeEdgeInfo;
                }

                // Fallback: return the first Edge window with useful information
                var firstValidWindow = edgeWindows.FirstOrDefault(w => !string.IsNullOrEmpty(w.WindowTitle));
                if (firstValidWindow != null)
                {
                    DebugLogger.WriteLine($"[EdgeBrowser] ?? Using fallback window: '{firstValidWindow.WindowTitle}'");
                    await EnrichWithAdditionalInfoAsync(firstValidWindow);
                    return firstValidWindow;
                }

                DebugLogger.WriteLine("[EdgeBrowser] ? No suitable Edge windows found");
                return null;
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ? Error getting Edge info: {ex.Message}");
                DebugLogger.WriteLine($"[EdgeBrowser] ? Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private List<Process> GetEdgeProcesses()
        {
            try
            {
                var edgeProcesses = new List<Process>();
                
                // Look for various Edge process names
                var edgeProcessNames = new[] { "msedge", "MicrosoftEdge", "microsoft edge" };
                
                foreach (var processName in edgeProcessNames)
                {
                    try
                    {
                        var processes = Process.GetProcessesByName(processName);
                        edgeProcesses.AddRange(processes);
                        DebugLogger.WriteLine($"[EdgeBrowser] ?? Found {processes.Length} processes for '{processName}'");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.WriteLine($"[EdgeBrowser] ?? Error getting processes for '{processName}': {ex.Message}");
                    }
                }

                return edgeProcesses.Distinct().ToList();
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ? Error enumerating Edge processes: {ex.Message}");
                return new List<Process>();
            }
        }

        private async Task<List<EdgeBrowserInfo>> GetEdgeWindowsAsync(List<Process> edgeProcesses)
        {
            var edgeWindows = new List<EdgeBrowserInfo>();
            var processIds = edgeProcesses.Select(p => (uint)p.Id).ToHashSet();

            await Task.Run(() =>
            {
                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hWnd))
                            return true; // Continue enumeration

                        GetWindowThreadProcessId(hWnd, out uint processId);
                        if (!processIds.Contains(processId))
                            return true; // Not an Edge window

                        int length = GetWindowTextLength(hWnd);
                        if (length == 0)
                            return true; // No window title

                        var title = new StringBuilder(length + 1);
                        GetWindowText(hWnd, title, title.Capacity);
                        var windowTitle = title.ToString();

                        if (string.IsNullOrEmpty(windowTitle) || windowTitle.Contains("Microsoft Edge"))
                            return true; // Skip generic Edge windows

                        var edgeInfo = new EdgeBrowserInfo
                        {
                            WindowTitle = windowTitle,
                            ProcessId = (int)processId,
                            WindowHandle = hWnd,
                            PageTitle = ExtractPageTitleFromWindowTitle(windowTitle)
                        };

                        edgeWindows.Add(edgeInfo);
                        DebugLogger.WriteLine($"[EdgeBrowser] ?? Window found: '{windowTitle}' (PID: {processId})");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.WriteLine($"[EdgeBrowser] ?? Error processing window: {ex.Message}");
                    }

                    return true; // Continue enumeration
                }, IntPtr.Zero);
            });

            return edgeWindows;
        }

        private async Task<EdgeBrowserInfo?> FindActiveMediaWindowAsync(List<EdgeBrowserInfo> windows)
        {
            try
            {
                // Check for windows with media-related content
                var mediaKeywords = new[] 
                {
                    "youtube", "spotify", "netflix", "twitch", "prime video", "hulu", "disney+",
                    "music", "video", "stream", "play", "watch", "listen"
                };

                // Priority 1: Window with media keywords and likely media content
                foreach (var window in windows)
                {
                    var titleLower = window.WindowTitle?.ToLowerInvariant() ?? "";
                    var hasMediaKeywords = mediaKeywords.Any(keyword => titleLower.Contains(keyword));
                    
                    if (hasMediaKeywords)
                    {
                        DebugLogger.WriteLine($"[EdgeBrowser] ?? Media window found: '{window.WindowTitle}'");
                        window.IsAudioPlaying = await CheckForAudioActivity(window);
                        return window;
                    }
                }

                // Priority 2: Currently focused window
                var foregroundWindow = GetForegroundWindow();
                var focusedWindow = windows.FirstOrDefault(w => w.WindowHandle == foregroundWindow);
                if (focusedWindow != null)
                {
                    DebugLogger.WriteLine($"[EdgeBrowser] ?? Focused window: '{focusedWindow.WindowTitle}'");
                    focusedWindow.IsAudioPlaying = await CheckForAudioActivity(focusedWindow);
                    return focusedWindow;
                }

                // Priority 3: Window with the most interesting title
                var mostInteresting = windows
                    .Where(w => !string.IsNullOrEmpty(w.WindowTitle))
                    .OrderByDescending(w => w.WindowTitle!.Length)
                    .FirstOrDefault();

                if (mostInteresting != null)
                {
                    mostInteresting.IsAudioPlaying = await CheckForAudioActivity(mostInteresting);
                    return mostInteresting;
                }

                return null;
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ? Error finding active media window: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> CheckForAudioActivity(EdgeBrowserInfo window)
        {
            try
            {
                // This is a placeholder - we could implement more sophisticated audio detection
                // For now, we'll check if the window title suggests media activity
                var titleLower = window.WindowTitle?.ToLowerInvariant() ?? "";
                var audioIndicators = new[] { "?", "?", "playing", "paused", "?", "?", "??", "??" };
                
                return audioIndicators.Any(indicator => titleLower.Contains(indicator));
            }
            catch
            {
                return false;
            }
        }

        private async Task EnrichWithAdditionalInfoAsync(EdgeBrowserInfo edgeInfo)
        {
            try
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ?? Enriching info for window: '{edgeInfo.WindowTitle}'");

                // Extract metadata from window title
                await ExtractMetadataFromTitleAsync(edgeInfo);

                // Try to get additional process information
                await GetProcessInfoAsync(edgeInfo);

                // Log all extracted information
                LogExtractedInfo(edgeInfo);
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ?? Error enriching window info: {ex.Message}");
            }
        }

        private async Task ExtractMetadataFromTitleAsync(EdgeBrowserInfo edgeInfo)
        {
            try
            {
                var title = edgeInfo.WindowTitle ?? "";
                var metadata = edgeInfo.ExtractedMetadata;

                // Extract service information
                if (title.Contains(" - YouTube Music") || title.Contains(" | YouTube Music"))
                {
                    metadata["Service"] = "YouTube Music";
                    metadata["MediaType"] = "Audio";
                    var cleanTitle = title.Replace(" - YouTube Music", "").Replace(" | YouTube Music", "").Trim();
                    metadata["CleanTitle"] = cleanTitle;
                }
                else if (title.Contains(" - YouTube") || title.Contains(" | YouTube"))
                {
                    metadata["Service"] = "YouTube";
                    metadata["MediaType"] = "Video";
                    var cleanTitle = title.Replace(" - YouTube", "").Replace(" | YouTube", "").Trim();
                    metadata["CleanTitle"] = cleanTitle;
                }
                else if (title.Contains(" | Prime Video") || title.Contains(" - Prime Video"))
                {
                    metadata["Service"] = "Prime Video";
                    metadata["MediaType"] = "Video";
                    var cleanTitle = title.Replace(" | Prime Video", "").Replace(" - Prime Video", "").Trim();
                    metadata["CleanTitle"] = cleanTitle;
                }
                else if (title.Contains(" | Netflix") || title.Contains(" - Netflix"))
                {
                    metadata["Service"] = "Netflix";
                    metadata["MediaType"] = "Video";
                    var cleanTitle = title.Replace(" | Netflix", "").Replace(" - Netflix", "").Trim();
                    metadata["CleanTitle"] = cleanTitle;
                }
                else if (title.Contains(" | Spotify") || title.Contains(" - Spotify"))
                {
                    metadata["Service"] = "Spotify";
                    metadata["MediaType"] = "Audio";
                    var cleanTitle = title.Replace(" | Spotify", "").Replace(" - Spotify", "").Trim();
                    metadata["CleanTitle"] = cleanTitle;
                }

                // Try to extract artist/title separation for music
                if (metadata.ContainsKey("MediaType") && metadata["MediaType"] == "Audio")
                {
                    var cleanTitle = metadata.GetValueOrDefault("CleanTitle", title);
                    if (cleanTitle.Contains(" - "))
                    {
                        var parts = cleanTitle.Split(new[] { " - " }, 2, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            metadata["Artist"] = parts[0].Trim();
                            metadata["SongTitle"] = parts[1].Trim();
                        }
                    }
                    else if (cleanTitle.Contains(" by "))
                    {
                        var parts = cleanTitle.Split(new[] { " by " }, 2, StringSplitOptions.None);
                        if (parts.Length == 2)
                        {
                            metadata["SongTitle"] = parts[0].Trim();
                            metadata["Artist"] = parts[1].Trim();
                        }
                    }
                }

                metadata["WindowTitle"] = title;
                metadata["ExtractionTimestamp"] = DateTime.Now.ToString("HH:mm:ss.fff");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ? Error extracting metadata: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private async Task GetProcessInfoAsync(EdgeBrowserInfo edgeInfo)
        {
            try
            {
                var process = Process.GetProcessById(edgeInfo.ProcessId);
                var metadata = edgeInfo.ExtractedMetadata;

                metadata["ProcessName"] = process.ProcessName;
                metadata["ProcessId"] = process.Id.ToString();
                metadata["ProcessStartTime"] = process.StartTime.ToString("HH:mm:ss");
                metadata["WorkingSet"] = $"{process.WorkingSet64 / (1024 * 1024)} MB";

                // Try to get command line arguments (may not always work due to permissions)
                try
                {
                    var startInfo = process.StartInfo;
                    if (!string.IsNullOrEmpty(startInfo.Arguments))
                    {
                        metadata["CommandLineArgs"] = startInfo.Arguments;
                    }
                }
                catch
                {
                    // Ignore command line access errors
                }

                // Get window position and size
                if (GetWindowRect(edgeInfo.WindowHandle, out RECT rect))
                {
                    metadata["WindowPosition"] = $"{rect.Left},{rect.Top}";
                    metadata["WindowSize"] = $"{rect.Right - rect.Left}x{rect.Bottom - rect.Top}";
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ?? Error getting process info: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private string ExtractPageTitleFromWindowTitle(string windowTitle)
        {
            try
            {
                // Remove common Edge window suffixes
                var suffixes = new[] { " - Microsoft Edge", " - Edge", " — Microsoft Edge", " — Edge" };
                var cleanTitle = windowTitle;
                
                foreach (var suffix in suffixes)
                {
                    if (cleanTitle.EndsWith(suffix))
                    {
                        cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - suffix.Length).Trim();
                        break;
                    }
                }

                return cleanTitle;
            }
            catch
            {
                return windowTitle;
            }
        }

        private void LogExtractedInfo(EdgeBrowserInfo edgeInfo)
        {
            try
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ?? EXTRACTED EDGE BROWSER INFORMATION:");
                DebugLogger.WriteLine($"[EdgeBrowser]   Window Title: '{edgeInfo.WindowTitle}'");
                DebugLogger.WriteLine($"[EdgeBrowser]   Page Title: '{edgeInfo.PageTitle}'");
                DebugLogger.WriteLine($"[EdgeBrowser]   Process ID: {edgeInfo.ProcessId}");
                DebugLogger.WriteLine($"[EdgeBrowser]   Window Handle: {edgeInfo.WindowHandle}");
                DebugLogger.WriteLine($"[EdgeBrowser]   Audio Playing: {edgeInfo.IsAudioPlaying}");

                if (edgeInfo.ExtractedMetadata.Any())
                {
                    DebugLogger.WriteLine($"[EdgeBrowser]   ?? Extracted Metadata:");
                    foreach (var kvp in edgeInfo.ExtractedMetadata.OrderBy(x => x.Key))
                    {
                        DebugLogger.WriteLine($"[EdgeBrowser]     {kvp.Key}: '{kvp.Value}'");
                    }
                }
                else
                {
                    DebugLogger.WriteLine($"[EdgeBrowser]   ?? No metadata extracted");
                }

                // Summary
                var service = edgeInfo.ExtractedMetadata.GetValueOrDefault("Service", "Unknown");
                var mediaType = edgeInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "Unknown");
                var cleanTitle = edgeInfo.ExtractedMetadata.GetValueOrDefault("CleanTitle", edgeInfo.PageTitle ?? "");
                
                DebugLogger.WriteLine($"[EdgeBrowser] ?? SUMMARY: {service} | {mediaType} | '{cleanTitle}'");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[EdgeBrowser] ? Error logging extracted info: {ex.Message}");
            }
        }
    }
}
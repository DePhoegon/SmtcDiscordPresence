using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SmtcDiscordPresence.Services
{
    public class BrowserInfo
    {
        public string? WindowTitle { get; set; }
        public string? Url { get; set; }
        public string? PageTitle { get; set; }
        public int ProcessId { get; set; }
        public IntPtr WindowHandle { get; set; }
        public bool IsAudioPlaying { get; set; }
        public string BrowserType { get; set; } = "";
        public Dictionary<string, string> ExtractedMetadata { get; set; } = new();
    }

    public sealed class BrowserService
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

        private readonly Dictionary<string, string[]> _browserProcessNames = new()
        {
            ["Edge"] = new[] { "msedge", "MicrosoftEdge" },
            ["Chrome"] = new[] { "chrome", "googlechrome", "chromium" },
            ["Firefox"] = new[] { "firefox", "mozilla" }
        };

        public async Task<BrowserInfo?> GetCurrentBrowserInfoAsync(string sourceApp)
        {
            try
            {
                var browserType = DetectBrowserType(sourceApp);
                if (string.IsNullOrEmpty(browserType))
                {
                    DebugLogger.WriteLine("[Browser] ? Browser type not detected or supported");
                    return null;
                }

                DebugLogger.WriteLine($"[Browser] ?? Starting {browserType} browser information extraction...");
                
                // Get browser processes
                var browserProcesses = GetBrowserProcesses(browserType);
                if (!browserProcesses.Any())
                {
                    DebugLogger.WriteLine($"[Browser] ? No {browserType} processes found");
                    return null;
                }

                DebugLogger.WriteLine($"[Browser] ?? Found {browserProcesses.Count} {browserType} processes");

                // Get browser windows
                var browserWindows = await GetBrowserWindowsAsync(browserProcesses, browserType);
                if (!browserWindows.Any())
                {
                    DebugLogger.WriteLine($"[Browser] ? No {browserType} windows found");
                    return null;
                }

                DebugLogger.WriteLine($"[Browser] ?? Found {browserWindows.Count} {browserType} windows");

                // Find the active/focused browser window or one with media
                var activeBrowserInfo = await FindActiveMediaWindowAsync(browserWindows);
                if (activeBrowserInfo != null)
                {
                    DebugLogger.WriteLine($"[Browser] ? Found active media window: '{activeBrowserInfo.WindowTitle}'");
                    await EnrichWithAdditionalInfoAsync(activeBrowserInfo);
                    return activeBrowserInfo;
                }

                // Fallback: return the first browser window with useful information
                var firstValidWindow = browserWindows.FirstOrDefault(w => !string.IsNullOrEmpty(w.WindowTitle));
                if (firstValidWindow != null)
                {
                    DebugLogger.WriteLine($"[Browser] ?? Using fallback window: '{firstValidWindow.WindowTitle}'");
                    await EnrichWithAdditionalInfoAsync(firstValidWindow);
                    return firstValidWindow;
                }

                DebugLogger.WriteLine($"[Browser] ? No suitable {browserType} windows found");
                return null;
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Browser] ? Error getting browser info: {ex.Message}");
                DebugLogger.WriteLine($"[Browser] ? Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private string DetectBrowserType(string sourceApp)
        {
            var appLower = sourceApp.ToLowerInvariant();
            
            if (appLower.Contains("edge") || appLower.Contains("msedge") || appLower.Contains("microsoftedge"))
                return "Edge";
            if (appLower.Contains("chrome") || appLower.Contains("chromium"))
                return "Chrome";
            if (appLower.Contains("firefox") || appLower.Contains("mozilla"))
                return "Firefox";
                
            return "";
        }

        private List<Process> GetBrowserProcesses(string browserType)
        {
            try
            {
                var processes = new List<Process>();
                
                if (_browserProcessNames.TryGetValue(browserType, out var processNames))
                {
                    foreach (var processName in processNames)
                    {
                        try
                        {
                            var browserProcesses = Process.GetProcessesByName(processName);
                            processes.AddRange(browserProcesses);
                            DebugLogger.WriteLine($"[Browser] ?? Found {browserProcesses.Length} processes for '{processName}'");
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.WriteLine($"[Browser] ?? Error getting processes for '{processName}': {ex.Message}");
                        }
                    }
                }

                return processes.Distinct().ToList();
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Browser] ? Error enumerating {browserType} processes: {ex.Message}");
                return new List<Process>();
            }
        }

        private async Task<List<BrowserInfo>> GetBrowserWindowsAsync(List<Process> processes, string browserType)
        {
            var windows = new List<BrowserInfo>();
            var processIds = processes.Select(p => (uint)p.Id).ToHashSet();

            await Task.Run(() =>
            {
                EnumWindows((hWnd, lParam) =>
                {
                    try
                    {
                        if (!IsWindowVisible(hWnd))
                            return true;

                        GetWindowThreadProcessId(hWnd, out uint processId);
                        if (!processIds.Contains(processId))
                            return true;

                        int length = GetWindowTextLength(hWnd);
                        if (length == 0)
                            return true;

                        var title = new StringBuilder(length + 1);
                        GetWindowText(hWnd, title, title.Capacity);
                        var windowTitle = title.ToString();

                        if (string.IsNullOrEmpty(windowTitle) || IsGenericBrowserWindow(windowTitle, browserType))
                            return true;

                        var browserInfo = new BrowserInfo
                        {
                            WindowTitle = windowTitle,
                            ProcessId = (int)processId,
                            WindowHandle = hWnd,
                            BrowserType = browserType,
                            PageTitle = ExtractPageTitleFromWindowTitle(windowTitle, browserType)
                        };

                        windows.Add(browserInfo);
                        DebugLogger.WriteLine($"[Browser] ?? {browserType} window found: '{windowTitle}' (PID: {processId})");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.WriteLine($"[Browser] ?? Error processing window: {ex.Message}");
                    }

                    return true;
                }, IntPtr.Zero);
            });

            return windows;
        }

        private bool IsGenericBrowserWindow(string windowTitle, string browserType)
        {
            var genericPatterns = browserType switch
            {
                "Edge" => new[] { "Microsoft Edge", "New Tab", "Edge" },
                "Chrome" => new[] { "Google Chrome", "New Tab", "Chrome" },
                "Firefox" => new[] { "Mozilla Firefox", "Firefox", "New Tab" },
                _ => new[] { "New Tab" }
            };

            return genericPatterns.Any(pattern => windowTitle.Equals(pattern, StringComparison.OrdinalIgnoreCase));
        }

        private async Task<BrowserInfo?> FindActiveMediaWindowAsync(List<BrowserInfo> windows)
        {
            try
            {
                var mediaKeywords = new[] 
                {
                    "youtube", "spotify", "netflix", "twitch", "prime video", "hulu", "disney+",
                    "music", "video", "stream", "play", "watch", "listen"
                };

                // Priority 1: Window with media keywords
                foreach (var window in windows)
                {
                    var titleLower = window.WindowTitle?.ToLowerInvariant() ?? "";
                    var hasMediaKeywords = mediaKeywords.Any(keyword => titleLower.Contains(keyword));
                    
                    if (hasMediaKeywords)
                    {
                        DebugLogger.WriteLine($"[Browser] ?? Media window found: '{window.WindowTitle}'");
                        window.IsAudioPlaying = await CheckForAudioActivity(window);
                        return window;
                    }
                }

                // Priority 2: Currently focused window
                var foregroundWindow = GetForegroundWindow();
                var focusedWindow = windows.FirstOrDefault(w => w.WindowHandle == foregroundWindow);
                if (focusedWindow != null)
                {
                    DebugLogger.WriteLine($"[Browser] ?? Focused window: '{focusedWindow.WindowTitle}'");
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
                DebugLogger.WriteLine($"[Browser] ? Error finding active media window: {ex.Message}");
                return null;
            }
        }

        private async Task<bool> CheckForAudioActivity(BrowserInfo window)
        {
            try
            {
                var titleLower = window.WindowTitle?.ToLowerInvariant() ?? "";
                var audioIndicators = new[] { "?", "?", "playing", "paused", "?", "?", "??", "??" };
                
                return audioIndicators.Any(indicator => titleLower.Contains(indicator));
            }
            catch
            {
                return false;
            }
        }

        private async Task EnrichWithAdditionalInfoAsync(BrowserInfo browserInfo)
        {
            try
            {
                DebugLogger.WriteLine($"[Browser] ?? Enriching info for {browserInfo.BrowserType} window: '{browserInfo.WindowTitle}'");

                // Extract metadata from window title
                await ExtractMetadataFromTitleAsync(browserInfo);

                // Try to get additional process information
                await GetProcessInfoAsync(browserInfo);

                // Log all extracted information
                LogExtractedInfo(browserInfo);
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Browser] ?? Error enriching window info: {ex.Message}");
            }
        }

        private async Task ExtractMetadataFromTitleAsync(BrowserInfo browserInfo)
        {
            try
            {
                var title = browserInfo.WindowTitle ?? "";
                var metadata = browserInfo.ExtractedMetadata;

                DebugLogger.WriteLine($"[Browser] ?? Analyzing {browserInfo.BrowserType} window title: '{title}'");

                // Enhanced service detection - look for service patterns BEFORE browser suffixes
                var serviceDetected = false;

                // YouTube Music - highest priority for audio
                if (title.Contains("YouTube Music"))
                {
                    metadata["Service"] = "YouTube Music";
                    metadata["MediaType"] = "Audio";
                    serviceDetected = true;
                    DebugLogger.WriteLine($"[Browser] ? YouTube Music detected in title");
                }
                // YouTube - after checking for YouTube Music
                else if (title.Contains("YouTube") && !title.Contains("YouTube Music"))
                {
                    metadata["Service"] = "YouTube";
                    metadata["MediaType"] = "Video";
                    serviceDetected = true;
                    DebugLogger.WriteLine($"[Browser] ? YouTube detected in title");
                }
                // Prime Video
                else if (title.Contains("Prime Video"))
                {
                    metadata["Service"] = "Prime Video";
                    metadata["MediaType"] = "Video";
                    serviceDetected = true;
                    DebugLogger.WriteLine($"[Browser] ? Prime Video detected in title");
                }
                // Netflix
                else if (title.Contains("Netflix"))
                {
                    metadata["Service"] = "Netflix";
                    metadata["MediaType"] = "Video";
                    serviceDetected = true;
                    DebugLogger.WriteLine($"[Browser] ? Netflix detected in title");
                }
                // Spotify
                else if (title.Contains("Spotify"))
                {
                    metadata["Service"] = "Spotify";
                    metadata["MediaType"] = "Audio";
                    serviceDetected = true;
                    DebugLogger.WriteLine($"[Browser] ? Spotify detected in title");
                }
                // Twitch
                else if (title.Contains("Twitch"))
                {
                    metadata["Service"] = "Twitch";
                    metadata["MediaType"] = "Video";
                    serviceDetected = true;
                    DebugLogger.WriteLine($"[Browser] ? Twitch detected in title");
                }
                // Hulu
                else if (title.Contains("Hulu"))
                {
                    metadata["Service"] = "Hulu";
                    metadata["MediaType"] = "Video";
                    serviceDetected = true;
                    DebugLogger.WriteLine($"[Browser] ? Hulu detected in title");
                }
                // Disney+
                else if (title.Contains("Disney+"))
                {
                    metadata["Service"] = "Disney+";
                    metadata["MediaType"] = "Video";
                    serviceDetected = true;
                    DebugLogger.WriteLine($"[Browser] ? Disney+ detected in title");
                }

                if (!serviceDetected)
                {
                    DebugLogger.WriteLine($"[Browser] ? No service detected in title");
                    metadata["Service"] = "Unknown";
                    metadata["MediaType"] = "Unknown";
                }

                // Extract clean title by removing browser-specific suffixes
                var cleanTitle = ExtractCleanTitle(title, browserInfo.BrowserType);
                metadata["CleanTitle"] = cleanTitle;
                metadata["OriginalWindowTitle"] = title;
                metadata["BrowserType"] = browserInfo.BrowserType;
                metadata["ExtractionTimestamp"] = DateTime.Now.ToString("HH:mm:ss.fff");

                DebugLogger.WriteLine($"[Browser] ?? Clean title extracted: '{cleanTitle}'");

            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Browser] ? Error extracting metadata: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private string ExtractCleanTitle(string windowTitle, string browserType)
        {
            try
            {
                var cleanTitle = windowTitle;

                // Remove browser-specific suffixes
                var browserSuffixes = browserType switch
                {
                    "Edge" => new[] 
                    { 
                        " - Microsoft Edge", " — Microsoft Edge", " - Edge", " — Edge",
                        " and 1 more page - Personal - Microsoft\u2026 Edge",
                        " and 23 more pages - Personal - Microsoft\u2026 Edge"
                    },
                    "Chrome" => new[] 
                    { 
                        " - Google Chrome", " — Google Chrome", " - Chrome", " — Chrome" 
                    },
                    "Firefox" => new[] 
                    { 
                        " - Mozilla Firefox", " — Mozilla Firefox", " - Firefox", " — Firefox" 
                    },
                    _ => new string[0]
                };

                // Remove browser suffixes first
                foreach (var suffix in browserSuffixes)
                {
                    if (cleanTitle.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        cleanTitle = cleanTitle.Substring(0, cleanTitle.Length - suffix.Length).Trim();
                        break;
                    }
                }

                // Remove common patterns like "and X more pages - Personal - Microsoft... Edge"
                var patterns = new[]
                {
                    @" and \d+ more pages? - Personal - Microsoft.*",
                    @" and \d+ more pages?",
                    @" - Personal - Microsoft.*",
                    @" - Personal$"
                };

                foreach (var pattern in patterns)
                {
                    cleanTitle = System.Text.RegularExpressions.Regex.Replace(cleanTitle, pattern, "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
                }

                return cleanTitle;
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Browser] ? Error cleaning title: {ex.Message}");
                return windowTitle;
            }
        }

        private async Task GetProcessInfoAsync(BrowserInfo browserInfo)
        {
            try
            {
                var process = Process.GetProcessById(browserInfo.ProcessId);
                var metadata = browserInfo.ExtractedMetadata;

                metadata["ProcessName"] = process.ProcessName;
                metadata["ProcessId"] = process.Id.ToString();
                metadata["ProcessStartTime"] = process.StartTime.ToString("HH:mm:ss");
                metadata["WorkingSet"] = $"{process.WorkingSet64 / (1024 * 1024)} MB";

                // Get window position and size
                if (GetWindowRect(browserInfo.WindowHandle, out RECT rect))
                {
                    metadata["WindowPosition"] = $"{rect.Left},{rect.Top}";
                    metadata["WindowSize"] = $"{rect.Right - rect.Left}x{rect.Bottom - rect.Top}";
                }
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Browser] ?? Error getting process info: {ex.Message}");
            }

            await Task.CompletedTask;
        }

        private string ExtractPageTitleFromWindowTitle(string windowTitle, string browserType)
        {
            try
            {
                return ExtractCleanTitle(windowTitle, browserType);
            }
            catch
            {
                return windowTitle;
            }
        }

        private void LogExtractedInfo(BrowserInfo browserInfo)
        {
            try
            {
                DebugLogger.WriteLine($"[Browser] ?? EXTRACTED {browserInfo.BrowserType.ToUpper()} BROWSER INFORMATION:");
                DebugLogger.WriteLine($"[Browser]   Window Title: '{browserInfo.WindowTitle}'");
                DebugLogger.WriteLine($"[Browser]   Page Title: '{browserInfo.PageTitle}'");
                DebugLogger.WriteLine($"[Browser]   Browser Type: '{browserInfo.BrowserType}'");
                DebugLogger.WriteLine($"[Browser]   Process ID: {browserInfo.ProcessId}");
                DebugLogger.WriteLine($"[Browser]   Window Handle: {browserInfo.WindowHandle}");
                DebugLogger.WriteLine($"[Browser]   Audio Playing: {browserInfo.IsAudioPlaying}");

                if (browserInfo.ExtractedMetadata.Any())
                {
                    DebugLogger.WriteLine($"[Browser]   ?? Extracted Metadata:");
                    foreach (var kvp in browserInfo.ExtractedMetadata.OrderBy(x => x.Key))
                    {
                        DebugLogger.WriteLine($"[Browser]     {kvp.Key}: '{kvp.Value}'");
                    }
                }
                else
                {
                    DebugLogger.WriteLine($"[Browser]   ?? No metadata extracted");
                }

                // Summary
                var service = browserInfo.ExtractedMetadata.GetValueOrDefault("Service", "Unknown");
                var mediaType = browserInfo.ExtractedMetadata.GetValueOrDefault("MediaType", "Unknown");
                var cleanTitle = browserInfo.ExtractedMetadata.GetValueOrDefault("CleanTitle", browserInfo.PageTitle ?? "");
                
                DebugLogger.WriteLine($"[Browser] ?? SUMMARY: {service} | {mediaType} | '{cleanTitle}'");
            }
            catch (Exception ex)
            {
                DebugLogger.WriteLine($"[Browser] ? Error logging extracted info: {ex.Message}");
            }
        }
    }
}
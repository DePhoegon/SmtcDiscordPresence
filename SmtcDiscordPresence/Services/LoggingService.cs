using System;
using System.Text;
using System.Windows.Forms;

namespace SmtcDiscordPresence.Services
{
    public static class DebugLogger
    {
        private static readonly StringBuilder _logBuffer = new();
        private static readonly object _lock = new();
        private static Form? _logWindow;

        public static void WriteLine(string message)
        {
            lock (_lock)
            {
                var logEntry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                _logBuffer.AppendLine(logEntry);
                
                // Also output to system debug in case Visual Studio is attached
                System.Diagnostics.Debug.WriteLine(logEntry);
                
                // Keep only last 1000 lines to prevent memory issues
                var lines = _logBuffer.ToString().Split('\n');
                if (lines.Length > 1000)
                {
                    _logBuffer.Clear();
                    for (int i = lines.Length - 1000; i < lines.Length; i++)
                    {
                        _logBuffer.AppendLine(lines[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Clear the log buffer on application start
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _logBuffer.Clear();
                WriteLine("=== DEBUG LOG CLEARED - APPLICATION STARTING ===");
            }
        }

        public static void ShowLogWindow()
        {
            if (_logWindow != null && !_logWindow.IsDisposed)
            {
                _logWindow.BringToFront();
                return;
            }

            _logWindow = new Form
            {
                Text = "SMTC Discord Presence - Debug Log",
                Width = 800,
                Height = 600,
                StartPosition = FormStartPosition.CenterScreen
            };

            var textBox = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = new System.Drawing.Font("Consolas", 9)
            };

            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, __) =>
            {
                lock (_lock)
                {
                    var currentText = _logBuffer.ToString();
                    if (textBox.Text != currentText)
                    {
                        textBox.Text = currentText;
                        textBox.SelectionStart = textBox.TextLength;
                        textBox.ScrollToCaret();
                    }
                }
            };

            _logWindow.FormClosed += (_, __) => timer.Stop();
            _logWindow.Controls.Add(textBox);
            timer.Start();

            // Show current log content
            lock (_lock)
            {
                textBox.Text = _logBuffer.ToString();
                textBox.SelectionStart = textBox.TextLength;
                textBox.ScrollToCaret();
            }

            _logWindow.Show();
        }

        public static string GetLogContent()
        {
            lock (_lock)
            {
                return _logBuffer.ToString();
            }
        }

        public static void Dispose()
        {
            _logWindow?.Dispose();
        }
    }
}
using System;
using System.IO;
using System.Text.Json;
using SmtcDiscordPresence.Models;

namespace SmtcDiscordPresence.Services
{
    public static class ConfigurationService
    {
        private static AppConfiguration? _configuration;

        public static AppConfiguration LoadConfiguration()
        {
            if (_configuration != null)
                return _configuration;

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            
            // Try to load from secrets file first (if it exists)
            var secretsPath = Path.Combine(baseDirectory, "appsettings.secrets.json");
            if (File.Exists(secretsPath))
            {
                _configuration = LoadFromFile(secretsPath);
                return _configuration;
            }

            // Fall back to regular appsettings.json
            var settingsPath = Path.Combine(baseDirectory, "appsettings.json");
            if (File.Exists(settingsPath))
            {
                _configuration = LoadFromFile(settingsPath);
                return _configuration;
            }

            throw new FileNotFoundException("Configuration file not found. Please ensure appsettings.json or appsettings.secrets.json exists.");
        }

        private static AppConfiguration LoadFromFile(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                var config = JsonSerializer.Deserialize<AppConfiguration>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });

                if (config == null)
                    throw new InvalidOperationException("Configuration file could not be deserialized.");

                ValidateConfiguration(config);
                return config;
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Invalid JSON in configuration file '{path}': {ex.Message}", ex);
            }
        }

        private static void ValidateConfiguration(AppConfiguration config)
        {
            if (string.IsNullOrWhiteSpace(config.Discord.ClientId) || 
                config.Discord.ClientId == "YOUR_DISCORD_CLIENT_ID_HERE")
            {
                throw new InvalidOperationException("Discord Client ID is not configured. Please update your configuration file with a valid Discord Application Client ID.");
            }

            if (config.Settings.UpdateIntervalSeconds < 5)
            {
                throw new InvalidOperationException("Update interval must be at least 5 seconds to avoid rate limiting.");
            }
        }
    }
}
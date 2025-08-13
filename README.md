# SMTC Discord Presence

A Windows application that displays currently playing media information as Discord Rich Presence status.

## Setup

1. **Discord Application Setup**
   - Go to [Discord Developer Portal](https://discord.com/developers/applications)
   - Create a new application
   - Copy the Application ID (Client ID)

2. **Configuration**
   - Copy your Discord Application ID
   - Create `appsettings.secrets.json` in the same directory as the executable
   - Add your Discord Client ID:
   
   ```json
   {
     "Discord": {
       "ClientId": "YOUR_DISCORD_CLIENT_ID_HERE"
     }
   }
   ```

3. **Optional Settings**
   You can also customize these settings in your config file:
   
   ```json
   {
     "Discord": {
       "ClientId": "YOUR_DISCORD_CLIENT_ID_HERE"
     },
     "Settings": {
       "UpdateIntervalSeconds": 10,
       "ShowBalloonTips": true
     }
   }
   ```

## Configuration Files

- `appsettings.json` - Template configuration file (safe to commit to git)
- `appsettings.secrets.json` - Your actual configuration with real Client ID (ignored by git)
- `appsettings.secrets.json.example` - Example of the secrets file format

## Notes

- The application will first try to load `appsettings.secrets.json`, then fall back to `appsettings.json`
- Update interval must be at least 5 seconds to avoid Discord rate limiting
- The `appsettings.secrets.json` file is automatically ignored by git to keep your secrets safe
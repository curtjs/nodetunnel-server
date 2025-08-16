# NodeTunnel Server
This is the source code for the NodeTunnel relay server. Note that NodeTunnel is still very early in development, so you may encounter some issues.

### Setup
**This varies a lot depending on your setup, if you need help, please ask on Discord!**
1. Setup .NET on your server, this varies depending on your setup. See: https://learn.microsoft.com/en-us/dotnet/core/install/
2. Open ports 9999 for UDP and 9998 for TCP for both incoming and outgoing traffic
3. Clone this repository
4. Build & run the server (must be in `nodetunnel-server` directory)

   ```dotnet run --configuration Release```
5. The server should now be running! I would recommend making a `systemd` service to automatically run it. You can connect to it using your server's public IPv4 address

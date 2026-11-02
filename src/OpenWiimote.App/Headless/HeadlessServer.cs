using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenWiimote.App.Headless;

public static class HeadlessServer
{
    private const int MaxRequestBodyBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(string[] args)
    {
        LoadProjectEnvFile();
        var listenUrls = ResolveListenUrls(args);
        var allowAnonymous = IsTruthy(Environment.GetEnvironmentVariable("OPENWIIMOTE_ALLOW_ANONYMOUS"));
        var token = Environment.GetEnvironmentVariable("OPENWIIMOTE_ACCESS_TOKEN");
        if (string.IsNullOrWhiteSpace(token))
        {
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            if (allowAnonymous)
            {
                Console.WriteLine("OPENWIIMOTE_ACCESS_TOKEN was not set; generated a one-run access token that is not needed:");
            }
            else
            {
                Console.WriteLine("OPENWIIMOTE_ACCESS_TOKEN was not set; generated a one-run access token:");
            }
            Console.WriteLine(token);
        }

        if (allowAnonymous)
        {
            Console.WriteLine("OPENWIIMOTE_ALLOW_ANONYMOUS is set: every API route is open to anyone who can");
            Console.WriteLine("reach this port. Only do that on a network you trust.");
        }

        await using var service = new HeadlessWiimoteService();
        await service.StartAsync().ConfigureAwait(false);

        var webRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        var plans = listenUrls.Select(BuildPlan).ToList();
        using var listener = StartListener(plans);
        Console.WriteLine("OpenWiimote headless web service listening. Open one of these URLs in a browser:");
        foreach (var prefix in listener.Prefixes)
        {
            Console.WriteLine($"  {prefix.TrimEnd('/')}");
        }

        var reachable = DescribeReachableAddresses(plans);
        if (reachable.Count > 0)
        {
            Console.WriteLine("Reachable from other devices on the same network:");
            foreach (var address in reachable)
            {
                Console.WriteLine($"  {address}");
            }
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        try
        {
            while (!shutdown.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }

                _ = HandleAsync(context, service, token, allowAnonymous, webRoot);
            }
        }
        finally
        {
            try
            {
                listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static async Task HandleAsync(
        HttpListenerContext context,
        HeadlessWiimoteService service,
        string token,
        bool allowAnonymous,
        string webRoot)
    {
        try
        {
            await DispatchAsync(context, service, token, allowAnonymous, webRoot).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new { error = ex.Message }).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                context.Response.OutputStream.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static async Task DispatchAsync(
        HttpListenerContext context,
        HeadlessWiimoteService service,
        string token,
        bool allowAnonymous,
        string webRoot)
    {
        var request = context.Request;
        var path = request.Url?.AbsolutePath ?? "/";
        var method = request.HttpMethod.ToUpperInvariant();

        if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            await DispatchApiAsync(context, service, token, allowAnonymous, path, method).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(method, "GET", StringComparison.Ordinal) && !string.Equals(method, "HEAD", StringComparison.Ordinal))
        {
            await WriteJsonAsync(context, HttpStatusCode.MethodNotAllowed, new { error = "Only GET is supported for static content." }).ConfigureAwait(false);
            return;
        }

        await ServeStaticAsync(context, webRoot, path).ConfigureAwait(false);
    }

    private static async Task DispatchApiAsync(
        HttpListenerContext context,
        HeadlessWiimoteService service,
        string token,
        bool allowAnonymous,
        string path,
        string method)
    {
        var isAuthRoute = path.Equals("/api/auth", StringComparison.OrdinalIgnoreCase);
        var isHealthRoute = path.Equals("/api/health", StringComparison.OrdinalIgnoreCase);

        // Health and the token handshake stay open so the page can find out whether it
        // needs a token at all. Everything else is gated unless the operator opted out.
        if (!allowAnonymous && !isAuthRoute && !isHealthRoute && !HasValidToken(context.Request, token))
        {
            context.Response.AddHeader("WWW-Authenticate", "Bearer");
            await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new { error = "A valid access token is required." }).ConfigureAwait(false);
            return;
        }

        // Requests made by links, prefetchers and crawlers must not trigger actions.
        // Keep this check after authentication so protected routes do not disclose state.
        var action = path.ToLowerInvariant() is "/api/profiles/reload" or "/api/scan" or
            "/api/disconnect" or "/api/emulation" or "/api/rumble" or "/api/leds" or
            "/api/profile" or "/api/deviceprofile" or "/api/settings" or "/api/tilt" or
            "/api/mapping" or "/api/mapping/extension" or "/api/mapping/stick" or
            "/api/logs/clear" or "/api/pair" or "/api/bluetooth/pair";
        if (action && method != "POST")
        {
            context.Response.AddHeader("Allow", "POST");
            await WriteJsonAsync(context, HttpStatusCode.MethodNotAllowed,
                new { error = "Only POST is supported." }).ConfigureAwait(false);
            return;
        }

        switch (path.ToLowerInvariant())
        {
            case "/api/health":
                await WriteJsonAsync(context, HttpStatusCode.OK, new { status = "ok", authRequired = !allowAnonymous }).ConfigureAwait(false);
                return;

            case "/api/auth":
                if (allowAnonymous)
                {
                    await WriteJsonAsync(context, HttpStatusCode.OK, new { authenticated = true }).ConfigureAwait(false);
                    return;
                }

                var authRequest = await ReadJsonAsync<AccessRequest>(context.Request).ConfigureAwait(false);
                if (authRequest is not null && TokenEquals(authRequest.Token, token))
                {
                    await WriteJsonAsync(context, HttpStatusCode.OK, new { authenticated = true }).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(context, HttpStatusCode.Unauthorized, new { authenticated = false }).ConfigureAwait(false);
                return;

            case "/api/state":
                await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                return;

            case "/api/profiles":
                if (!string.Equals(method, "POST", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, HttpStatusCode.OK, service.GetProfileNames()).ConfigureAwait(false);
                    return;
                }

                // An empty body is allowed: the service then picks the next free name and
                // starts the profile from the default layout.
                var createRequest = await ReadJsonAsync<CreateProfileRequest>(context.Request).ConfigureAwait(false);
                if (context.Request.HasEntityBody && createRequest is null)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest,
                        new { error = "Invalid profile JSON body." }).ConfigureAwait(false);
                    return;
                }
                try
                {
                    service.CreateProfile(createRequest?.Name, createRequest?.CopyOf);
                    await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }).ConfigureAwait(false);
                }

                return;

            case "/api/profiles/reload":
                await WriteJsonAsync(context, HttpStatusCode.OK, service.ReloadProfiles()).ConfigureAwait(false);
                return;

            case "/api/scan":
                await service.ScanAsync().ConfigureAwait(false);
                await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                return;

            case "/api/disconnect":
                await service.DisconnectAllAsync().ConfigureAwait(false);
                await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                return;

            case "/api/emulation":
                var emulationRequest = await ReadJsonAsync<BooleanRequest>(context.Request).ConfigureAwait(false);
                if (emulationRequest is null)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "A JSON body with 'enabled' is required." }).ConfigureAwait(false);
                    return;
                }

                service.SetEmulation(emulationRequest.Enabled);
                await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                return;

            case "/api/rumble":
                var rumbleRequest = await ReadJsonAsync<BooleanRequest>(context.Request).ConfigureAwait(false);
                if (rumbleRequest is null)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "A JSON body with 'enabled' is required." }).ConfigureAwait(false);
                    return;
                }

                service.SetRumble(rumbleRequest.Enabled);
                await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                return;

            case "/api/leds":
                var ledRequest = await ReadJsonAsync<IntegerRequest>(context.Request).ConfigureAwait(false);
                if (ledRequest is null)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "A JSON body with 'value' is required." }).ConfigureAwait(false);
                    return;
                }

                service.SetLedMask(ledRequest.Value, ledRequest.DeviceKey);
                await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                return;

            case "/api/profile":
                var profileRequest = await ReadJsonAsync<ProfileRequest>(context.Request).ConfigureAwait(false);
                if (profileRequest is null || string.IsNullOrWhiteSpace(profileRequest.Name))
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "A JSON body with 'name' is required." }).ConfigureAwait(false);
                    return;
                }

                try
                {
                    if (profileRequest.Delete)
                    {
                        service.DeleteProfile(profileRequest.Name);
                    }
                    else if (!string.IsNullOrWhiteSpace(profileRequest.RenameTo))
                    {
                        service.RenameProfile(profileRequest.Name, profileRequest.RenameTo);
                    }
                    else
                    {
                        service.SetProfile(profileRequest.Name);
                    }

                    await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }).ConfigureAwait(false);
                }

                return;

            case "/api/deviceprofile":
                var deviceProfileRequest = await ReadJsonAsync<DeviceProfileRequest>(context.Request).ConfigureAwait(false);
                if (deviceProfileRequest is null || string.IsNullOrWhiteSpace(deviceProfileRequest.DeviceKey))
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "A JSON body with 'deviceKey' is required." }).ConfigureAwait(false);
                    return;
                }

                try
                {
                    service.AssignDeviceProfile(deviceProfileRequest.DeviceKey, deviceProfileRequest.Profile);
                    await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }).ConfigureAwait(false);
                }

                return;

            case "/api/settings":
                var settingsRequest = await ReadJsonAsync<SettingsRequest>(context.Request).ConfigureAwait(false);
                if (settingsRequest is null)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "A JSON settings body is required." }).ConfigureAwait(false);
                    return;
                }

                service.UpdateSettings(settingsRequest);
                await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                return;

            case "/api/tilt":
                var tiltRequest = await ReadJsonAsync<TiltRequest>(context.Request).ConfigureAwait(false);
                if (tiltRequest is null)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "A JSON tilt body is required." }).ConfigureAwait(false);
                    return;
                }

                try
                {
                    service.UpdateTiltSettings(tiltRequest);
                    await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new { error = ex.Message }).ConfigureAwait(false);
                }

                return;

            case "/api/mapping":
                if (!string.Equals(method, "POST", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, HttpStatusCode.MethodNotAllowed, new { error = "Only POST is supported." }).ConfigureAwait(false);
                    return;
                }

                var mappingRequest = await ReadJsonAsync<MappingRequest>(context.Request).ConfigureAwait(false);
                try
                {
                    service.UpdateMapping(mappingRequest!);
                    await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }).ConfigureAwait(false);
                }

                return;

            case "/api/mapping/extension":
                if (!string.Equals(method, "POST", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, HttpStatusCode.MethodNotAllowed, new { error = "Only POST is supported." }).ConfigureAwait(false);
                    return;
                }

                var extMappingRequest = await ReadJsonAsync<ExtensionMappingRequest>(context.Request).ConfigureAwait(false);
                try
                {
                    service.UpdateExtensionMapping(extMappingRequest!);
                    await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }).ConfigureAwait(false);
                }

                return;

            case "/api/mapping/stick":
                if (!string.Equals(method, "POST", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, HttpStatusCode.MethodNotAllowed, new { error = "Only POST is supported." }).ConfigureAwait(false);
                    return;
                }

                var stickRequest = await ReadJsonAsync<ExtensionStickRequest>(context.Request).ConfigureAwait(false);
                try
                {
                    service.UpdateExtensionStick(stickRequest!);
                    await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                }
                catch (ArgumentException ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = ex.Message }).ConfigureAwait(false);
                }

                return;

            case "/api/logs/clear":
                service.ClearLogs();
                await WriteJsonAsync(context, HttpStatusCode.OK, service.GetSnapshot()).ConfigureAwait(false);
                return;

            case "/api/paireddevices":
                var pairedDevices = await GetPairedBluetoothDevicesAsync().ConfigureAwait(false);
                await WriteJsonAsync(context, HttpStatusCode.OK, pairedDevices).ConfigureAwait(false);
                return;

            case "/api/bluetooth/paired":
            case "/api/bluetooth/paireddevices":
                var pairedDevices2 = await GetPairedBluetoothDevicesAsync().ConfigureAwait(false);
                await WriteJsonAsync(context, HttpStatusCode.OK, pairedDevices2).ConfigureAwait(false);
                return;

            case "/api/bluetooth/scan":
            case "/api/scan/bluetooth":
                if (!string.Equals(method, "POST", StringComparison.Ordinal) && !string.Equals(method, "GET", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, HttpStatusCode.MethodNotAllowed, new { error = "Only GET or POST is supported." }).ConfigureAwait(false);
                    return;
                }
                var discovered = await ScanBluetoothDevicesAsync().ConfigureAwait(false);
                await WriteJsonAsync(context, HttpStatusCode.OK, discovered).ConfigureAwait(false);
                return;

            case "/api/pair":
            case "/api/bluetooth/pair":
                if (!string.Equals(method, "POST", StringComparison.Ordinal))
                {
                    await WriteJsonAsync(context, HttpStatusCode.MethodNotAllowed, new { error = "Only POST is supported." }).ConfigureAwait(false);
                    return;
                }
                var pairRequest = await ReadJsonAsync<PairRequest>(context.Request).ConfigureAwait(false);
                if (pairRequest == null || string.IsNullOrWhiteSpace(pairRequest.Address))
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "A JSON body with 'address' is required." }).ConfigureAwait(false);
                    return;
                }

                if (!IsValidBluetoothAddress(pairRequest.Address))
                {
                    await WriteJsonAsync(context, HttpStatusCode.BadRequest, new { error = "The address must look like AA:BB:CC:DD:EE:FF." }).ConfigureAwait(false);
                    return;
                }
                try
                {
                    var result = await PairBluetoothDeviceAsync(pairRequest.Address).ConfigureAwait(false);
                    await WriteJsonAsync(context, result.Success ? HttpStatusCode.OK : HttpStatusCode.InternalServerError, result).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await WriteJsonAsync(context, HttpStatusCode.InternalServerError, new { error = ex.Message }).ConfigureAwait(false);
                }
                return;

            default:
                await WriteJsonAsync(context, HttpStatusCode.NotFound, new { error = "Unknown API route." }).ConfigureAwait(false);
                return;
        }
    }

    private static async Task ServeStaticAsync(HttpListenerContext context, string webRoot, string path)
    {
        if (!WebRootPathResolver.TryResolve(webRoot, path, out var combined))
        {
            await WriteJsonAsync(context, HttpStatusCode.Forbidden, new { error = "Invalid path." }).ConfigureAwait(false);
            return;
        }

        if (Directory.Exists(combined))
        {
            combined = Path.Combine(combined, "index.html");
        }

        if (!File.Exists(combined))
        {
            await WriteJsonAsync(context, HttpStatusCode.NotFound, new { error = "Not found." }).ConfigureAwait(false);
            return;
        }

        var bytes = await File.ReadAllBytesAsync(combined).ConfigureAwait(false);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = ContentTypeFor(combined);
        context.Response.ContentLength64 = bytes.Length;
        context.Response.AddHeader("Cache-Control", "no-cache");
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    private static string ContentTypeFor(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "text/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            ".woff2" => "font/woff2",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Accepts the token from the <c>Authorization</c> header or from a <c>?token=</c>
    /// query parameter, so a link can open the page ready to poll.
    /// </summary>
    private static bool HasValidToken(HttpListenerRequest request, string token)
    {
        var supplied = request.Headers["Authorization"];
        if (!string.IsNullOrEmpty(supplied))
        {
            const string prefix = "Bearer ";
            if (supplied.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                TokenEquals(supplied[prefix.Length..].Trim(), token))
            {
                return true;
            }
        }

        var query = request.Url?.Query;
        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            if (kv.Length == 2 && string.Equals(kv[0], "token", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (TokenEquals(Uri.UnescapeDataString(kv[1]), token))
                    {
                        return true;
                    }
                }
                catch (UriFormatException)
                {
                    return false;
                }
            }
        }

        return false;
    }

    private static bool IsTruthy(string? value)
    {
        return value?.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpListenerRequest request) where T : class
    {
        if (!request.HasEntityBody)
        {
            return null;
        }

        if (request.ContentLength64 > MaxRequestBodyBytes)
        {
            return null;
        }

        try
        {
            using var limited = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await request.InputStream.ReadAsync(buffer).ConfigureAwait(false)) > 0)
            {
                if (limited.Length + read > MaxRequestBodyBytes)
                {
                    return null;
                }

                limited.Write(buffer, 0, read);
            }

            if (limited.Length == 0)
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(limited.ToArray(), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task WriteJsonAsync(HttpListenerContext context, HttpStatusCode status, object payload)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json; charset=utf-8";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies <c>OPENWIIMOTE_*</c> entries from the nearest <c>.env</c> file. A variable
    /// already set in the process environment stays as it is, and <c>--urls</c> still wins
    /// over <c>OPENWIIMOTE_URLS</c>.
    /// </summary>
    private static void LoadProjectEnvFile()
    {
        var path = FindEnvFile();
        if (path is null)
        {
            return;
        }

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var key = line[..split].Trim();
            if (!key.StartsWith("OPENWIIMOTE_", StringComparison.Ordinal) ||
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
            {
                continue;
            }

            var value = line[(split + 1)..].Trim();
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindEnvFile()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            for (var depth = 0; dir is not null && depth < 6; depth++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static List<string> ResolveListenUrls(string[] args)
    {
        var urls = new List<string>();
        var environmentUrls = Environment.GetEnvironmentVariable("OPENWIIMOTE_URLS");

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--urls=", StringComparison.OrdinalIgnoreCase))
            {
                urls.Add(arg["--urls=".Length..]);
            }
            else if (string.Equals(arg, "--urls", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                urls.Add(args[i + 1]);
                i++;
            }
        }

        if (urls.Count == 0 && !string.IsNullOrWhiteSpace(environmentUrls))
        {
            urls.AddRange(environmentUrls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        if (urls.Count == 0)
        {
            urls.Add("http://127.0.0.1:8080");
        }

        return urls.Select(NormalizeUrl).ToList();
    }

    private static string NormalizeUrl(string url)
    {
        var trimmed = url.Trim().TrimEnd('/');
        if (trimmed.Contains("0.0.0.0", StringComparison.Ordinal))
        {
            return trimmed.Replace("0.0.0.0", "+", StringComparison.Ordinal);
        }

        if (trimmed.Contains("[::]", StringComparison.Ordinal) || trimmed.Contains("*", StringComparison.Ordinal))
        {
            return trimmed;
        }

        return trimmed;
    }

    private static List<string> BuildPlan(string url)
    {
        return ExpandPrefixes(url).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static HttpListener StartListener(IEnumerable<List<string>> plans)
    {
        var listener = new HttpListener();
        foreach (var prefix in plans.SelectMany(plan => plan).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            listener.Prefixes.Add(prefix);
        }

        try
        {
            listener.Start();
            return listener;
        }
        catch
        {
            listener.Close();
            throw;
        }
    }

    /// <summary>
    /// The URLs a device on the same network can actually open. A wildcard prefix binds
    /// every interface, so it is reported as the addresses behind it rather than as the
    /// literal <c>+</c> that nobody can type into a browser.
    /// </summary>
    private static List<string> DescribeReachableAddresses(IEnumerable<List<string>> plans)
    {
        var addresses = new List<string>();
        foreach (var prefix in plans.SelectMany(plan => plan).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (prefix.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
                prefix.Contains("[::1]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var (host, portAndPath) = SplitPrefix(prefix);
            if (!IsWildcardHost(host))
            {
                addresses.Add(prefix.TrimEnd('/'));
                continue;
            }

            addresses.AddRange(LocalAddresses().Select(address => $"http://{address}{portAndPath}".TrimEnd('/')));
        }

        return addresses.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Splits <c>http://host:port/path</c> into the host and the <c>:port/path</c> tail.</summary>
    private static (string Host, string PortAndPath) SplitPrefix(string prefix)
    {
        var authority = prefix.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            ? prefix["http://".Length..]
            : prefix;

        var slash = authority.IndexOf('/');
        var tail = slash < 0 ? "" : authority[slash..];
        if (slash >= 0)
        {
            authority = authority[..slash];
        }

        var colon = authority.LastIndexOf(':');
        var bracket = authority.IndexOf(']');
        if (colon <= 0 || (bracket >= 0 && colon < bracket))
        {
            return (authority, tail);
        }

        return (authority[..colon], authority[colon..] + tail);
    }

    private static bool IsWildcardHost(string host)
    {
        return host is "+" or "*" or "0.0.0.0" or "::" or "[::]";
    }

    /// <summary>The IPv4 addresses of the interfaces that are up, loopback excluded.</summary>
    private static IEnumerable<string> LocalAddresses()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                             ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(u => u.Address.ToString())
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ExpandPrefixes(string url)
    {
        var normalized = NormalizeUrl(url);
        if (!normalized.EndsWith('/'))
        {
            normalized += "/";
        }

        if (!normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            yield return normalized;
            yield break;
        }

        var hostPart = normalized["http://".Length..];
        var hostEnd = hostPart.IndexOf('/', StringComparison.Ordinal);
        if (hostEnd < 0)
        {
            hostEnd = hostPart.Length;
        }

        var host = hostPart.Substring(0, hostEnd);
        var path = hostPart.Substring(hostEnd);

        var expandedHosts = new List<string>();

        if (IsWildcardHost(host))
        {
            expandedHosts.Add("127.0.0.1");
            expandedHosts.Add("[::1]");
            expandedHosts.AddRange(LocalAddresses());
        }
        else
        {
            expandedHosts.Add(host);
        }

        foreach (var expandedHost in expandedHosts.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var port = "";
            var colonIdx = expandedHost.IndexOf(':', StringComparison.Ordinal);
            var prefixHost = expandedHost;
            if (colonIdx >= 0)
            {
                port = expandedHost.Substring(colonIdx);
                prefixHost = expandedHost.Substring(0, colonIdx);
            }
            else
            {
                if (normalized.Contains(":", StringComparison.Ordinal))
                {
                    var urlPortStart = normalized.LastIndexOf(':');
                    var urlPortEnd = normalized.IndexOf('/', urlPortStart);
                    if (urlPortEnd < 0) urlPortEnd = normalized.Length;
                    port = normalized.Substring(urlPortStart, urlPortEnd - urlPortStart);
                }
            }

            if (prefixHost.Contains(':') && !prefixHost.StartsWith("[", StringComparison.Ordinal))
            {
                prefixHost = $"[{prefixHost}]";
            }

            yield return $"http://{prefixHost}{port}{path}";
        }
    }

    private static bool IsLocalRequest(HttpListenerRequest request)
    {
        try
        {
            var remote = request.RemoteEndPoint;
            if (remote != null)
            {
                var ip = remote.Address;
                if (IPAddress.IsLoopback(ip)) return true;
                if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any)) return true;
            }
            var local = request.LocalEndPoint;
            if (local != null && IPAddress.IsLoopback(local.Address)) return true;
            var userHost = request.UserHostAddress;
            if (!string.IsNullOrEmpty(userHost))
            {
                if (userHost.Contains("127.0.0.1") || userHost.Contains("[::1]") || userHost.Contains("localhost"))
                {
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static bool TokenEquals(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        return suppliedBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }

    private static async Task<List<BluetoothDeviceInfo>> GetPairedBluetoothDevicesAsync()
    {
        // Try helper binary first (setuid/sudo helper for privileged operations)
        var helperResult = await TryRunHelperAsync(new[] { "list-paired" }).ConfigureAwait(false);
        if (helperResult.Success && !string.IsNullOrWhiteSpace(helperResult.Output))
        {
            var parsed = ParseBluetoothDevices(helperResult.Output);
            if (parsed.Count > 0)
            {
                parsed = await EnrichDeviceInfoAsync(parsed).ConfigureAwait(false);
                return parsed;
            }
        }

        // Try bluetoothctl directly
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var result = await RunProcessAsync("bluetoothctl", "paired-devices").ConfigureAwait(false);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
            {
                var parsed = ParseBluetoothDevices(result.Output);
                if (parsed.Count > 0)
                {
                    parsed = await EnrichDeviceInfoAsync(parsed).ConfigureAwait(false);
                    return parsed;
                }
            }
            // Also try devices
            var devicesResult = await RunProcessAsync("bluetoothctl", "devices Paired").ConfigureAwait(false);
            if (devicesResult.Success && !string.IsNullOrWhiteSpace(devicesResult.Output))
            {
                var parsed = ParseBluetoothDevices(devicesResult.Output);
                if (parsed.Count > 0)
                {
                    parsed = await EnrichDeviceInfoAsync(parsed).ConfigureAwait(false);
                    return parsed;
                }
            }
        }

        // Try one more time with devices command
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var devicesResult = await RunProcessAsync("bluetoothctl", "devices").ConfigureAwait(false);
            if (devicesResult.Success && !string.IsNullOrWhiteSpace(devicesResult.Output))
            {
                var parsed = ParseBluetoothDevices(devicesResult.Output);
                if (parsed.Count > 0)
                {
                    parsed = await EnrichDeviceInfoAsync(parsed).ConfigureAwait(false);
                    return parsed;
                }
            }
        }
        return new List<BluetoothDeviceInfo>();
    }

    private static async Task<List<BluetoothDeviceInfo>> EnrichDeviceInfoAsync(List<BluetoothDeviceInfo> devices)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || devices.Count == 0)
        {
            return devices;
        }
        var enriched = new List<BluetoothDeviceInfo>(devices.Count);
        foreach (var device in devices)
        {
            try
            {
                var info = await RunProcessAsync("bluetoothctl", $"info {device.Address}").ConfigureAwait(false);
                var output = info.Output ?? string.Empty;
                var paired = output.Contains("Paired: yes", StringComparison.OrdinalIgnoreCase);
                var connected = output.Contains("Connected: yes", StringComparison.OrdinalIgnoreCase);
                var trusted = output.Contains("Trusted: yes", StringComparison.OrdinalIgnoreCase);
                var status = connected ? "Connected" : (paired ? "Paired" : (trusted ? "Trusted" : "Available"));
                enriched.Add(device with { Paired = paired, Connected = connected, Trusted = trusted, Status = status });
            }
            catch
            {
                enriched.Add(device);
            }
        }
        return enriched;
    }

    private static async Task<List<BluetoothDeviceInfo>> ScanBluetoothDevicesAsync()
    {
        // Try helper binary first
        var helperResult = await TryRunHelperAsync(new[] { "scan" }).ConfigureAwait(false);
        if (helperResult.Success && !string.IsNullOrWhiteSpace(helperResult.Output))
        {
            var parsed = ParseBluetoothDevices(helperResult.Output);
            if (parsed.Count > 0)
            {
                return parsed;
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Start scan briefly
            await RunProcessAsync("bluetoothctl", "scan on").ConfigureAwait(false);
            await Task.Delay(3000).ConfigureAwait(false);
            await RunProcessAsync("bluetoothctl", "scan off").ConfigureAwait(false);
            var result = await RunProcessAsync("bluetoothctl", "devices").ConfigureAwait(false);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
            {
                var parsed = ParseBluetoothDevices(result.Output);
                if (parsed.Count > 0)
                {
                    return parsed;
                }
            }
        }

        // Fall back to paired devices if scan finds nothing
        return await GetPairedBluetoothDevicesAsync().ConfigureAwait(false);
    }

    private static Task<PairResult> PairBluetoothDeviceAsync(string address)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return Task.FromResult(new PairResult(false, "Bluetooth pairing is not supported on this platform."));
        return PairBluetoothDeviceAsync(address, RunProcessAsync);
    }

    internal static async Task<PairResult> PairBluetoothDeviceAsync(string address,
        Func<string, string, Task<ProcessResult>> run)
    {
        address = address.Trim().ToUpperInvariant();
        if (!IsValidBluetoothAddress(address))
            return new PairResult(false, "The address must look like AA:BB:CC:DD:EE:FF.");

        Task<ProcessResult> Info() => run("bluetoothctl", $"info {address}");
        static bool HasFlag(ProcessResult result, string flag) => result.Success &&
            result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Any(line => string.Equals(line.Trim(), flag + ": yes", StringComparison.OrdinalIgnoreCase));

        var initial = await Info().ConfigureAwait(false);
        if (HasFlag(initial, "Connected")) return new PairResult(true, "The remote is already connected.");
        var paired = HasFlag(initial, "Paired");

        // Reconnect first. Never erase a saved bond as a recovery side effect.
        await run("bluetoothctl", $"--timeout 15 connect {address}").ConfigureAwait(false);
        if (HasFlag(await Info().ConfigureAwait(false), "Connected"))
            return new PairResult(true, "Connected to the remote.");
        if (paired)
            return new PairResult(false, "The saved pairing was kept. Wake the remote with a button, then try Connect again.");
        if (!initial.Success)
            return new PairResult(false, "Unable to confirm the remote's pairing state. Press SYNC, scan, then try again.");

        await run("bluetoothctl", $"--timeout 20 pair {address}").ConfigureAwait(false);
        await run("bluetoothctl", $"trust {address}").ConfigureAwait(false);
        await run("bluetoothctl", $"--timeout 15 connect {address}").ConfigureAwait(false);
        if (HasFlag(await Info().ConfigureAwait(false), "Connected"))
            return new PairResult(true, "Connected to the remote.");
        return new PairResult(false, "The remote is not connected. Press SYNC so its lights flash, then try again.");
    }

    private static async Task<HelperResult> TryRunHelperAsync(string[] args)
    {
        string[] helperPaths = new[]
        {
            "/usr/local/bin/openwiimote-helper",
            "/usr/bin/openwiimote-helper",
            Path.Combine(AppContext.BaseDirectory, "openwiimote-helper"),
            "openwiimote-helper"
        };

        foreach (var helperPath in helperPaths)
        {
            if (helperPath.Contains(Path.DirectorySeparatorChar) && !File.Exists(helperPath))
            {
                continue;
            }
            try
            {
                var result = await RunProcessAsync(helperPath, string.Join(" ", args)).ConfigureAwait(false);
                if (result.Success || !string.IsNullOrWhiteSpace(result.Output))
                {
                    return new HelperResult(result.Success, result.Output, result.Error);
                }
            }
            catch
            {
                // Try next helper
            }
        }
        return new HelperResult(false, string.Empty, string.Empty);
    }

    private static async Task<ProcessResult> RunProcessAsync(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                return new ProcessResult(false, string.Empty, "Failed to start process");
            }
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                await process.WaitForExitAsync().ConfigureAwait(false);
                await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
                return new ProcessResult(false, string.Empty, "Bluetooth command timed out after 30 seconds.");
            }
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode == 0, outputTask.Result, errorTask.Result);
        }
        catch (Exception ex)
        {
            return new ProcessResult(false, string.Empty, ex.Message);
        }
    }

    private static List<BluetoothDeviceInfo> ParseBluetoothDevices(string output)
    {
        var devices = new List<BluetoothDeviceInfo>();
        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            // Skip lines with RSSI, CHG, NEW, etc.
            if (line.Contains("RSSI") || line.Contains("[CHG]") || line.Contains("[NEW]") || line.Contains("ManufacturerData"))
            {
                continue;
            }
            // bluetoothctl paired-devices format: Device AA:BB:CC:DD:EE:FF Name
            if (line.Contains("Device ", StringComparison.OrdinalIgnoreCase))
            {
                var idx = line.IndexOf("Device ", StringComparison.OrdinalIgnoreCase);
                var rest = line.Substring(idx + 7).Trim();
                // Remove any RSSI info if present
                if (rest.Contains("RSSI")) continue;
                var parts = rest.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 1)
                {
                    var address = parts[0];
                    var name = parts.Length >= 2 ? parts[1].Trim() : address;
                    // Filter out non-name garbage
                    if (name.Contains("RSSI") || name.Contains("0xffffff")) name = address;
                    if (IsValidBluetoothAddress(address))
                    {
                        devices.Add(new BluetoothDeviceInfo(address, name));
                    }
                }
            }
            else if (IsValidBluetoothAddress(line.Trim()))
            {
                devices.Add(new BluetoothDeviceInfo(line.Trim(), line.Trim()));
            }
        }
        return devices;
    }

    private static bool IsValidBluetoothAddress(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        // Simple check for MAC address format
        return System.Text.RegularExpressions.Regex.IsMatch(address.Trim(), "^([0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$");
    }
}

public sealed record PairRequest(string? Address);
public sealed record BluetoothDeviceInfo(string Address, string Name, string? Status = null, bool Paired = false, bool Connected = false, bool Trusted = false);
public sealed record PairResult(bool Success, string Message);
internal sealed record HelperResult(bool Success, string Output, string Error);
internal sealed record ProcessResult(bool Success, string Output, string Error);

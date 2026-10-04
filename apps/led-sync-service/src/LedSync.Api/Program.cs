using LedSync.Api.Contracts;
using LedSync.Application.Capture;
using LedSync.Application.Profiles;
using LedSync.Application.Sync;
using LedSync.Domain.Models;
using LedSync.Infrastructure.Capture;
using LedSync.Infrastructure.Persistence;
using LedSync.Infrastructure.Sync;
using System.Net.WebSockets;

var builder = WebApplication.CreateBuilder(args);
// The service is a desktop companion, not a LAN server; keep every API route on loopback.
builder.WebHost.UseUrls("http://127.0.0.1:5078");
builder.Services.AddCors(options => options.AddPolicy("desktop-client", policy =>
    policy.SetIsOriginAllowed(origin =>
            origin is "http://localhost:4200" or "http://127.0.0.1:4200" or "aurasync://app")
        .AllowAnyHeader()
        .AllowAnyMethod()));
builder.Services.AddSingleton<IProfileRepository, JsonProfileRepository>();
builder.Services.AddSingleton<ProfileService>();
builder.Services.AddSingleton<IDisplayProvider, WindowsDisplayProvider>();
builder.Services.AddSingleton<ISerialPortProvider, WindowsSerialPortProvider>();
builder.Services.AddSingleton<WindowsAudioCaptureProvider>();
builder.Services.AddSingleton<IAudioDeviceProvider>(services => services.GetRequiredService<WindowsAudioCaptureProvider>());
builder.Services.AddSingleton<IAudioCaptureProvider>(services => services.GetRequiredService<WindowsAudioCaptureProvider>());
builder.Services.AddSingleton<ILedOutput, WindowsSerialLedOutput>();
builder.Services.AddSingleton<ISyncRuntime, SerialSyncRuntime>();

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiErrors");
    if (exception is not null)
        logger.LogError(exception, "Unhandled API error for {Path}", context.Request.Path);

    // Preserve actionable client errors while avoiding internal exception details for unexpected failures.
    var (status, title, detail) = exception switch
    {
        ArgumentException =>
            (StatusCodes.Status400BadRequest, "Invalid request", exception.Message),
        InvalidOperationException =>
            (StatusCodes.Status409Conflict, "Operation unavailable", exception.Message),
        PlatformNotSupportedException =>
            (StatusCodes.Status501NotImplemented, "Capability unavailable", exception.Message),
        TimeoutException =>
            (StatusCodes.Status503ServiceUnavailable, "LED controller timed out", exception.Message),
        IOException =>
            (StatusCodes.Status503ServiceUnavailable, "LED output unavailable", exception.Message),
        UnauthorizedAccessException =>
            (StatusCodes.Status503ServiceUnavailable, "Serial port access denied", exception.Message),
        _ =>
            (StatusCodes.Status500InternalServerError, "Unexpected service error", "The request could not be completed.")
    };

    context.Response.StatusCode = status;
    await context.Response.WriteAsJsonAsync(new
    {
        type = "about:blank",
        title,
        status,
        detail,
        instance = context.Request.Path.Value
    });
}));
app.UseCors("desktop-client");

var profileService = app.Services.GetRequiredService<ProfileService>();
var activeProfile = (await profileService.GetAllAsync(CancellationToken.None))
    .SingleOrDefault(profile => profile.IsActive);
app.Services.GetRequiredService<ISyncRuntime>().SetActiveProfile(activeProfile);

var api = app.MapGroup("/api/v1");

api.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "aurasync",
    version = typeof(Program).Assembly.GetName().Version?.ToString()
}));

api.MapGet("/profiles", async (ProfileService profiles, CancellationToken cancellationToken) =>
    Results.Ok(await profiles.GetAllAsync(cancellationToken)));

api.MapPost("/profiles", async (
    ProfileRequest request,
    ProfileService profiles,
    IDisplayProvider displays,
    ISyncRuntime runtime,
    CancellationToken cancellationToken) =>
{
    if (request.Settings?.CaptureMode == "screen" &&
        !await IsDisplayAvailable(request.Settings.DisplayId, displays, cancellationToken))
        return Results.ValidationProblem(DisplayUnavailable());

    var profile = await profiles.CreateAsync(request.Name, request.Settings, cancellationToken);
    if (profile.IsActive)
        runtime.SetActiveProfile(profile);
    return Results.Created($"/api/v1/profiles/{profile.Id}", profile);
});

api.MapPut("/profiles/{id:guid}", async (
    Guid id,
    ProfileRequest request,
    ProfileService profiles,
    IDisplayProvider displays,
    ISyncRuntime runtime,
    CancellationToken cancellationToken) =>
{
    var settings = request.Settings ?? throw new ArgumentException("Profile settings are required.");
    var status = runtime.GetStatus();
    if (status.IsRunning && status.ActiveProfileId == id.ToString() &&
        settings.LedDevice != runtime.Configuration.LedDevice)
        return Results.Conflict(new { detail = "Stop synchronization before changing the active profile's LED controller, port, baud rate, or layout." });
    if (status.IsRunning && status.ActiveProfileId == id.ToString() &&
        (settings.CaptureMode != runtime.Configuration.CaptureMode ||
         settings.AudioDeviceId != runtime.Configuration.AudioDeviceId))
        return Results.Conflict(new { detail = "Stop synchronization before changing the active profile's capture mode or audio output device." });

    if (settings.CaptureMode == "screen" &&
        !await IsDisplayAvailable(settings.DisplayId, displays, cancellationToken))
        return Results.ValidationProblem(DisplayUnavailable());

    var updated = await profiles.UpdateAsync(
        id,
        request.Name,
        settings,
        cancellationToken);
    if (updated is null)
        return Results.NotFound();
    if (updated.IsActive)
        runtime.SetActiveProfile(updated);
    return Results.Ok(updated);
});

api.MapDelete("/profiles/{id:guid}", async (
    Guid id,
    ProfileService profiles,
    ISyncRuntime runtime,
    CancellationToken cancellationToken) =>
{
    var status = runtime.GetStatus();
    if (status.IsRunning && status.ActiveProfileId == id.ToString())
        return Results.Conflict(new { detail = "Stop synchronization before deleting the active profile." });

    if (!await profiles.DeleteAsync(id, cancellationToken))
        return Results.NotFound();

    var activeProfile = (await profiles.GetAllAsync(cancellationToken)).SingleOrDefault(profile => profile.IsActive);
    runtime.SetActiveProfile(activeProfile);
    return Results.NoContent();
});

api.MapPost("/profiles/{id:guid}/activate", async (
    Guid id,
    ProfileService profiles,
    IDisplayProvider displays,
    ISyncRuntime runtime,
    CancellationToken cancellationToken) =>
{
    var candidate = (await profiles.GetAllAsync(cancellationToken)).SingleOrDefault(profile => profile.Id == id);
    if (candidate is null)
        return Results.NotFound();
    if (runtime.GetStatus().IsRunning && candidate.Settings.LedDevice != runtime.Configuration.LedDevice)
        return Results.Conflict(new { detail = "Stop synchronization before activating a profile with different LED controller settings." });
    if (runtime.GetStatus().IsRunning &&
        (candidate.Settings.CaptureMode != runtime.Configuration.CaptureMode ||
         candidate.Settings.AudioDeviceId != runtime.Configuration.AudioDeviceId))
        return Results.Conflict(new { detail = "Stop synchronization before activating a profile with a different capture mode or audio output device." });
    if (candidate.Settings.CaptureMode == "screen" &&
        !await IsDisplayAvailable(candidate.Settings.DisplayId, displays, cancellationToken))
        return Results.ValidationProblem(DisplayUnavailable());

    var profile = await profiles.ActivateAsync(id, cancellationToken);
    if (profile is null)
        return Results.NotFound();

    runtime.SetActiveProfile(profile);
    return Results.Ok(runtime.GetStatus());
});

api.MapGet("/displays", async (IDisplayProvider displays, CancellationToken cancellationToken) =>
    Results.Ok(await displays.GetDisplaysAsync(cancellationToken)));

api.MapGet("/serial/ports", async (ISerialPortProvider serialPorts, CancellationToken cancellationToken) =>
    Results.Ok(await serialPorts.GetPortsAsync(cancellationToken)));

api.MapGet("/audio/devices", async (IAudioDeviceProvider audio, CancellationToken cancellationToken) =>
    Results.Ok(await audio.GetAudioDevicesAsync(cancellationToken)));

api.MapGet("/sync/status", (ISyncRuntime runtime) => Results.Ok(runtime.GetStatus()));

api.MapPut("/sync/source", async (
    SyncSourceRequest request,
    IDisplayProvider displays,
    ISyncRuntime runtime,
    CancellationToken cancellationToken) =>
{
    if (!await IsDisplayAvailable(request.DisplayId, displays, cancellationToken))
        return Results.ValidationProblem(DisplayUnavailable());

    runtime.SetConfiguration(runtime.Configuration with { DisplayId = request.DisplayId });
    return Results.Ok(runtime.GetStatus());
});

api.MapPut("/sync/config", async (
    SyncConfigurationRequest request,
    IDisplayProvider displays,
    ISyncRuntime runtime,
    CancellationToken cancellationToken) =>
{
    if (request.CaptureMode == "screen" &&
        !await IsDisplayAvailable(request.DisplayId, displays, cancellationToken))
        return Results.ValidationProblem(DisplayUnavailable());

    runtime.SetConfiguration(new SyncConfiguration(
        request.DisplayId,
        request.FramesPerSecond,
        request.CaptureMode,
        request.AudioDeviceId,
        request.SoundMode)
    {
        LedDevice = request.LedDevice,
        AudioColors = request.AudioColors,
        CustomEffect = request.CustomEffect,
        CustomColors = request.CustomColors,
        CustomSpeed = request.CustomSpeed
    });
    return Results.Ok(runtime.GetStatus());
});

api.MapPost("/sync/start", async (
    SyncConfigurationRequest request,
    IDisplayProvider displays,
    ISyncRuntime runtime,
    CancellationToken cancellationToken) =>
{
    if (request.CaptureMode == "screen" &&
        !await IsDisplayAvailable(request.DisplayId, displays, cancellationToken))
        return Results.ValidationProblem(DisplayUnavailable());

    runtime.SetConfiguration(new SyncConfiguration(
        request.DisplayId,
        request.FramesPerSecond,
        request.CaptureMode,
        request.AudioDeviceId,
        request.SoundMode)
    {
        LedDevice = request.LedDevice,
        AudioColors = request.AudioColors,
        CustomEffect = request.CustomEffect,
        CustomColors = request.CustomColors,
        CustomSpeed = request.CustomSpeed
    });
    await runtime.StartAsync(cancellationToken);
    return Results.Accepted("/api/v1/sync/status", runtime.GetStatus());
});

api.MapPost("/sync/stop", async (ISyncRuntime runtime, CancellationToken cancellationToken) =>
{
    await runtime.StopAsync(cancellationToken);
    return Results.Ok(runtime.GetStatus());
});

api.MapGet("/sync/frames", async (HttpContext context, ISyncRuntime runtime, ILogger<Program> logger) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
        return Results.BadRequest("Connect using a WebSocket to send RGB frames.");

    var origin = context.Request.Headers.Origin.ToString();
    if (!string.IsNullOrEmpty(origin) &&
        origin is not ("http://localhost:4200" or "http://127.0.0.1:4200" or "aurasync://app"))
        return Results.StatusCode(StatusCodes.Status403Forbidden);

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var expectedLength = runtime.Configuration.LedDevice.LedCount * 3;
    var frame = new byte[expectedLength];
    try
    {
        while (socket.State == WebSocketState.Open && runtime.GetStatus().IsRunning)
        {
            var offset = 0;
            ValueWebSocketReceiveResult result;
            do
            {
                if (offset >= expectedLength)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.InvalidPayloadData,
                        "RGB frame has more bytes than the configured LED count.",
                        context.RequestAborted);
                    return Results.Empty;
                }

                result = await socket.ReceiveAsync(
                    frame.AsMemory(offset, expectedLength - offset),
                    context.RequestAborted);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (socket.State == WebSocketState.CloseReceived)
                    {
                        await socket.CloseOutputAsync(
                            socket.CloseStatus ?? WebSocketCloseStatus.NormalClosure,
                            socket.CloseStatusDescription,
                            CancellationToken.None);
                    }
                    return Results.Empty;
                }
                if (result.MessageType != WebSocketMessageType.Binary)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.InvalidMessageType,
                        "Send binary RGB frames.",
                        context.RequestAborted);
                    return Results.Empty;
                }
                offset += result.Count;
            } while (!result.EndOfMessage);

            if (offset != expectedLength)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.InvalidPayloadData,
                    $"RGB frames must contain exactly {expectedLength} bytes.",
                    context.RequestAborted);
                return Results.Empty;
            }

            await runtime.WriteFrameAsync(frame, context.RequestAborted);
        }
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
    }
    catch (WebSocketException exception)
    {
        logger.LogWarning(exception, "RGB frame WebSocket disconnected.");
    }
    catch (Exception exception)
    {
        logger.LogError(exception, "RGB frame processing failed.");
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(
                WebSocketCloseStatus.InternalServerError,
                "LED frame processing failed.",
                CancellationToken.None);
        }
    }
    finally
    {
        if (runtime.GetStatus().IsRunning)
        {
            try
            {
                await runtime.StopAsync(CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Unable to stop serial LED output after the frame stream ended.");
            }
        }
    }

    return Results.Empty;
});

app.Run();

static async Task<bool> IsDisplayAvailable(
    string? displayId,
    IDisplayProvider displays,
    CancellationToken cancellationToken)
{
    if (displayId is null)
        return true;
    var availableDisplays = await displays.GetDisplaysAsync(cancellationToken);
    return availableDisplays.Any(display => display.Id == displayId);
}

static Dictionary<string, string[]> DisplayUnavailable() => new()
{
    ["displayId"] = ["The requested display is not currently available."]
};

public partial class Program { }

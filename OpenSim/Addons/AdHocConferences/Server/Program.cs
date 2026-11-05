// Temporary multi-avatar IM conference registry for OpenSimulator.
// Run on the grid's ROBUST host, behind a private listener or a TLS reverse proxy.
// Configuration: ADHOC_SHARED_KEY and ASPNETCORE_URLS.
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

var sharedKey = Environment.GetEnvironmentVariable("ADHOC_SHARED_KEY");
if (string.IsNullOrWhiteSpace(sharedKey) || sharedKey.Length < 32)
    throw new InvalidOperationException("ADHOC_SHARED_KEY must contain at least 32 characters");

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
var conferences = new ConferenceRegistry();
var key = Encoding.UTF8.GetBytes(sharedKey);

app.MapGet("/health", () => Results.Ok(new { service = "opensim-adhoc-conferences" }));
app.MapPost("/conference", async (HttpRequest request) =>
{
    // Bind the signed payload to a region, an acting avatar and a short-lived
    // timestamp. Both signatures and time are checked before any mutation.
    string region = request.Headers["X-Adhoc-Region"].ToString();
    string actor = request.Headers["X-Adhoc-Agent"].ToString();
    string time = request.Headers["X-Adhoc-Time"].ToString();
    string signature = request.Headers["X-Adhoc-Signature"].ToString();
    if (!Guid.TryParse(region, out _) || !Guid.TryParse(actor, out Guid agent)
        || !long.TryParse(time, NumberStyles.None, CultureInfo.InvariantCulture, out long unix)
        || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix) > 300)
        return Results.Unauthorized();

    if (request.ContentLength > 32 * 1024)
        return Results.BadRequest(new { success = false, error = "Request too large" });
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    string body = await reader.ReadToEndAsync();
    if (Encoding.UTF8.GetByteCount(body) > 32 * 1024)
        return Results.BadRequest(new { success = false, error = "Request too large" });
    byte[] expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(region + "\n" + actor + "\n" + time + "\n" + body));
    byte[] provided;
    try { provided = Convert.FromHexString(signature); }
    catch (FormatException) { return Results.Unauthorized(); }
    if (!CryptographicOperations.FixedTimeEquals(expected, provided))
        return Results.Unauthorized();

    ConferenceRequest? command;
    try { command = System.Text.Json.JsonSerializer.Deserialize<ConferenceRequest>(body); }
    catch (System.Text.Json.JsonException) { return Results.BadRequest(new { success = false, error = "Invalid JSON" }); }
    if (command == null || !Guid.TryParse(command.Session, out Guid session) || session == Guid.Empty)
        return Results.BadRequest(new { success = false, error = "Invalid session" });

    return conferences.Execute(command, agent, session);
});

app.Run();

public sealed class ConferenceRequest
{
    public string Action { get; set; } = "";
    public string Session { get; set; } = "";
    public List<string> Invitees { get; set; } = new();
}

public sealed class ConferenceRegistry
{
    private sealed class Session
    {
        public Guid Owner;
        public Dictionary<Guid, bool> Members = new(); // true = joined; false = invited
        public DateTime Updated = DateTime.UtcNow;
    }

    private readonly ConcurrentDictionary<Guid, Session> sessions = new();
    private readonly object gate = new();
    private const int Limit = 25;

    public IResult Execute(ConferenceRequest request, Guid actor, Guid sessionId)
    {
        var invitees = new HashSet<Guid>();
        foreach (string id in request.Invitees ?? new List<string>())
        {
            if (!Guid.TryParse(id, out Guid parsed) || parsed == Guid.Empty)
                return Results.BadRequest(new { success = false, error = "Invalid invitee" });
            if (parsed != actor)
                invitees.Add(parsed);
        }
        if (invitees.Count >= Limit)
            return Results.BadRequest(new { success = false, error = "Too many invitees" });

        lock (gate)
        {
            // Expire inactive conversations to bound the memory footprint.
            foreach (var stale in sessions.Where(kv => DateTime.UtcNow - kv.Value.Updated > TimeSpan.FromHours(24)))
                sessions.TryRemove(stale.Key, out _);

            if (request.Action == "start")
            {
                if (invitees.Count == 0 || sessions.ContainsKey(sessionId))
                    return Results.Conflict(new { success = false, error = "Invalid or existing conference" });
                var created = new Session { Owner = actor };
                created.Members.Add(actor, true);
                foreach (Guid invitee in invitees)
                    created.Members.Add(invitee, false);
                sessions[sessionId] = created;
                return Snapshot(sessionId, created);
            }

            if (!sessions.TryGetValue(sessionId, out Session? session))
                return Results.NotFound(new { success = false, error = "Unknown conference" });
            if (!session.Members.TryGetValue(actor, out bool joined))
                return Results.Json(new { success = false, error = "Not a member" }, statusCode: 403);

            switch (request.Action)
            {
                case "members":
                    break;
                case "accept":
                    session.Members[actor] = true;
                    break;
                case "invite":
                    if (!joined)
                        return Results.Json(new { success = false, error = "Not joined" }, statusCode: 403);
                    if (session.Members.Count + invitees.Count(id => !session.Members.ContainsKey(id)) > Limit)
                        return Results.BadRequest(new { success = false, error = "Conference is full" });
                    foreach (Guid invitee in invitees)
                        session.Members.TryAdd(invitee, false);
                    break;
                case "leave":
                    session.Members.Remove(actor);
                    if (session.Members.Count == 0)
                        sessions.TryRemove(sessionId, out _);
                    break;
                default:
                    return Results.BadRequest(new { success = false, error = "Unknown action" });
            }
            session.Updated = DateTime.UtcNow;
            return Snapshot(sessionId, session);
        }
    }

    private static IResult Snapshot(Guid id, Session session) => Results.Ok(new
    {
        success = true,
        session = id,
        members = session.Members.Select(m => new { id = m.Key, joined = m.Value }).ToArray()
    });
}

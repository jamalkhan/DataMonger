using System.Buffers;
using System.Text.Json;
using DataMonger.Jsonl;

namespace DataMonger.Synth;

/// <summary>
/// Builds a single marketing_opt_in event for a profile (see Documentation/SynthesizeMyAccountOptins/EventSchema.md).
/// Unlike the session events this is NOT part of a shopping session: a profile has a 70% chance of one opt-in
/// event, dated anywhere in the last 4 years (but never before the profile's created_at, when it has one),
/// on a channel the profile's consent flags allow.
/// </summary>
public static class OptinEventBuilder
{
    public const string EventType = "marketing_opt_in";
    public const double OptinChance = 0.70;
    public const int MaxYearsBack = 4;
    public const string SourceSystem = "account_service";
    public const string SchemaVersion = "1.0";

    public static readonly IReadOnlyList<string> OptSources = new[] { "preference_center", "checkout", "email_signup" };
    public static readonly IReadOnlyList<string> PageSources = new[] { "account_page", "footer_form", "order_confirmation" };

    /// <summary>The contact an opt-in on <paramref name="channel"/> refers to; falls back to the email.</summary>
    public static string ContactValueFor(string channel, JsonElement profile, string userId, string email) => channel switch
    {
        "sms" => ProfileFields.ResolvePhone(profile) ?? email,
        "postal" => ProfileFields.ResolvePostalAddress(profile) ?? email,
        "push" => userId,
        _ => email,
    };

    /// <summary>Returns the rendered event JSON, or null if this profile didn't roll an opt-in (or has no email / consented channel).</summary>
    public static byte[]? BuildForProfile(JsonElement profile, Random rng, DateTimeOffset now)
    {
        if (profile.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var email = ProfileFields.ResolveEmail(profile);
        var userId = ProfileFields.ResolveUserId(profile);
        if (email is null || userId is null || rng.NextDouble() >= OptinChance)
        {
            return null;
        }
        var channels = ProfileFields.ConsentedChannels(profile);
        if (channels.Count == 0)
        {
            return null;
        }
        var channel = channels[rng.Next(channels.Count)];

        var timeZone = EventSession.ResolveTimeZone(ProfileFields.ResolveTimezone(profile));
        var nowLocal = TimeZoneInfo.ConvertTime(now, timeZone);
        var maxDays = MaxYearsBack * 365;
        if (ProfileFields.ResolveCreatedAt(profile) is { } createdAt)
        {
            maxDays = Math.Clamp((int)(now - createdAt).TotalDays, 0, maxDays);
        }
        var daysAgo = rng.Next(0, maxDays + 1);
        var secondsOfDay = rng.Next(0, 24 * 60 * 60);
        var anchorDate = nowLocal.Date.AddDays(-daysAgo);
        var consentAt = new DateTimeOffset(anchorDate, timeZone.GetUtcOffset(anchorDate)).AddSeconds(secondsOfDay);
        if (consentAt > now)
        {
            consentAt = now; // today's random time of day may be in the future; clamp rather than emit a future opt-in
        }

        var eventId = $"evt-{EventSession.HexId(rng)}";
        var optSource = OptSources[rng.Next(OptSources.Count)];
        var pageSource = PageSources[rng.Next(PageSources.Count)];

        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, JsonlRender.Options))
        {
            w.WriteStartObject();
            w.WriteString("schema_version", SchemaVersion);
            w.WriteString("event_id", eventId);
            w.WriteString("event_type", EventType);
            w.WriteString("event_time", SessionEventsBuilder.FormatTime(consentAt));
            w.WriteString("user_id", userId);
            w.WriteString("email", email);
            w.WriteString("storefront_id", ProfileFields.ResolveStorefrontId(profile));
            w.WriteString("source_system", SourceSystem);
            w.WriteStartObject("consent");
            w.WriteString("channel", channel);
            // The address the opt-in is for: sms -> phone, postal -> billing address, push -> user id,
            // email -> email; whenever the profile lacks the needed field the email is used.
            w.WriteString("contact_value", ContactValueFor(channel, profile, userId, email));
            w.WriteString("status", "subscribed");
            w.WriteString("subscribed_at", SessionEventsBuilder.FormatTime(consentAt));
            w.WriteString("opt_source", optSource);
            w.WriteString("page_source", pageSource);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return buffer.WrittenSpan.ToArray();
    }
}

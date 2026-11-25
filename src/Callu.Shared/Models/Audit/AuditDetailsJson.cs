using System.Text.Json.Nodes;

namespace Callu.Shared.Models.Audit;

/// <summary>Writes <see cref="AuditDetails"/> into the row's Details column.</summary>
// The keys and shapes are OpenAuditModel's own, so the sealer copies them into the event as they are
// rather than translating a second format. An empty fragment is left out: the schema rejects one.
public static class AuditDetailsJson
{
    public static string? Serialize(AuditDetails? details)
    {
        if (details is null) return null;

        var json = new JsonObject();

        if (details.Authorization is { } a)
        {
            var authorization = new JsonObject { ["decision"] = a.Decision };
            if (a.Engine is not null) authorization["engine"] = a.Engine;
            if (a.Policy is not null) authorization["policy"] = a.Policy;
            if (a.Permissions is { Count: > 0 } permissions)
                authorization["permissions"] = new JsonArray(permissions.Select(p => (JsonNode?)p).ToArray());
            json["authorization"] = authorization;
        }

        if (details.Reason is { } r && (r.Code is not null || r.Text is not null))
        {
            var reason = new JsonObject();
            if (r.Code is not null) reason["code"] = r.Code;
            if (r.Text is not null) reason["text"] = r.Text;
            json["reason"] = reason;
        }

        if (details.RelatedResources is { Count: > 0 } related)
            json["relatedResources"] = new JsonArray(related
                .Select(x => (JsonNode?)new JsonObject { ["type"] = x.Type, ["id"] = x.Id })
                .ToArray());

        if (details.Change is { } c)
        {
            var change = new JsonObject { ["type"] = c.Type };
            if (c.ChangedFields.Count > 0)
                change["changedFields"] = new JsonArray(c.ChangedFields.Select(f => (JsonNode?)f).ToArray());
            if (c.Before is not null) change["before"] = c.Before.DeepClone();
            if (c.After is not null) change["after"] = c.After.DeepClone();
            json["change"] = change;
        }

        if (details.Metadata is { Count: > 0 } metadata) json["metadata"] = metadata.DeepClone();

        return json.Count == 0 ? null : json.ToJsonString();
    }
}

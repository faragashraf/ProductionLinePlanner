using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProductionLinePlanner.Application.Requests;

public sealed class UserRoleAssignmentsRequest
{
    public string[] Roles { get; init; } = [];
}

public sealed class UserStatusRequest
{
    public bool IsActive { get; init; }
}

public sealed class UserPermissionOverrideRequest
{
    public string Permission { get; init; } = string.Empty;
    public string Effect { get; init; } = string.Empty;
}

public sealed class UserAuthorizationUpdateRequest
{
    public Guid[] RoleIds { get; init; } = [];
    public string[] DirectGrants { get; init; } = [];
    public string[] DirectDenies { get; init; } = [];
}

public sealed class RoleCreateRequest
{
    public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}

[JsonConverter(typeof(RoleUpdateRequestJsonConverter))]
public sealed class RoleUpdateRequest
{
    public string? Name { get; init; }
    // Omission preserves the existing value; null and whitespace explicitly clear it.
    public string? Description { get; init; }
    [JsonIgnore]
    public bool HasDescription { get; init; }
    public bool? IsActive { get; init; }
}

public sealed class RoleUpdateRequestJsonConverter : JsonConverter<RoleUpdateRequest>
{
    public override RoleUpdateRequest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Role update request must be an object.");
        }

        string? name = null;
        string? description = null;
        bool hasDescription = false;
        bool? isActive = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected a property name.");
            }

            var propertyName = reader.GetString();
            reader.Read();
            if (string.Equals(propertyName, "name", StringComparison.OrdinalIgnoreCase))
            {
                name = ReadNullableString(ref reader, "name");
            }
            else if (string.Equals(propertyName, "description", StringComparison.OrdinalIgnoreCase))
            {
                hasDescription = true;
                description = ReadNullableString(ref reader, "description");
            }
            else if (string.Equals(propertyName, "isActive", StringComparison.OrdinalIgnoreCase))
            {
                isActive = reader.TokenType == JsonTokenType.Null ? null : reader.GetBoolean();
            }
            else
            {
                reader.Skip();
            }
        }

        return new RoleUpdateRequest { Name = name, Description = description, HasDescription = hasDescription, IsActive = isActive };
    }

    public override void Write(Utf8JsonWriter writer, RoleUpdateRequest value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.Name is not null) writer.WriteString("name", value.Name);
        if (value.HasDescription) writer.WriteString("description", value.Description);
        if (value.IsActive.HasValue) writer.WriteBoolean("isActive", value.IsActive.Value);
        writer.WriteEndObject();
    }

    private static string? ReadNullableString(ref Utf8JsonReader reader, string propertyName) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            _ => throw new JsonException($"{propertyName} must be a string or null.")
        };
}

public sealed class RolePermissionSetRequest
{
    public string[] PermissionNames { get; init; } = [];
}

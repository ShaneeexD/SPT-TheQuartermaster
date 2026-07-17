using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;

namespace TheQuartermaster.Server.Utils.Json;

public class UpdJsonConverter : JsonConverter<Upd>
{
    public override Upd? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                var name = property.Name;
                // The client sends "tag" (lowercase); normalize to "Tag" for SPT's Upd record
                if (name == "tag")
                    name = "Tag";

                writer.WritePropertyName(name);
                property.Value.WriteTo(writer);
            }
            writer.WriteEndObject();
        }

        var json = Encoding.UTF8.GetString(stream.ToArray());
        var readOptions = new JsonSerializerOptions(options);
        readOptions.Converters.Remove(this);

        return JsonSerializer.Deserialize<Upd>(json, readOptions);
    }

    public override void Write(Utf8JsonWriter writer, Upd value, JsonSerializerOptions options)
    {
        var writeOptions = new JsonSerializerOptions(options);
        writeOptions.Converters.Remove(this);

        var json = JsonSerializer.Serialize(value, writeOptions);
        using var document = JsonDocument.Parse(json);

        writer.WriteStartObject();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var name = property.Name;
            // The game client expects the tag key to be lowercase "tag"
            if (name == "Tag")
                name = "tag";

            writer.WritePropertyName(name);
            property.Value.WriteTo(writer);
        }
        writer.WriteEndObject();
    }
}

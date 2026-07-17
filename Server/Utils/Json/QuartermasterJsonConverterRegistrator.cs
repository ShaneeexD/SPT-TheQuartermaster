using System.Collections.Generic;
using System.Text.Json.Serialization;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Utils.Json;

namespace TheQuartermaster.Server.Utils.Json;

[Injectable(InjectionType.Singleton)]
public class QuartermasterJsonConverterRegistrator : IJsonConverterRegistrator
{
    public IEnumerable<JsonConverter> GetJsonConverters()
    {
        // UpdJsonConverter removed: it was converting "Tag" to "tag" (lowercase) on serialization,
        // but the client's GClass1911.CreateItem expects "Tag" (PascalCase) to match the field name.
        // The server's default System.Text.Json serialization already produces PascalCase "Tag".
        yield break;
    }
}

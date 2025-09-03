# Instructions for migrating from Newtonsoft.Json to System.Text.Json in .NET projects.

## Scope

When you are asked to migrate a project from `Newtonsoft.Json` to `System.Text.Json` you need to determine for which projects you need to do it.
If a single project is specified - do it for that project only. If you are asked to do it for a solution, migrate all projects in the solution 
that reference `Newtonsoft.Json`. If you don't know which projects to migrate, ask the user.

## Things to consider while doing migration

- NuGet package names, assembly names, projects names or other dependencies names are case insensitive(!). You ***must take it into account*** when doing something 
  with project dependencies, like searching for dependencies or when removing them from projects etc.

## Planning

For each project that needs to be migrated, you need to do the following:

- Find projects depending on `Newtonsoft.Json` package or assembly reference (when searching for projects, if some projects are not part of the  
  solution or you could not find the project, notify user and continue with other projects).

## Execution

***Important***: when running steps in this section you must not pause, you must continue until you are done with all steps or you are truly unable to 
continue and need user's interaction (you will be penalized if you stop unnecessarily).

Keep in mind information in the next section about differences and follow these steps in the order they are specified (you will be penalized if you do steps 
below in wrong order or skip any of them):

1. For each project that has an explicit package dependency to `Newtonsoft.Json` in the project file of some imported MSBuild targets (some 
   project could receive package dependencies transitively, so avoid adding new package dependencies for such projects), do the following:

- Remove the `Newtonsoft.Json` package reference and assembly reference from the project file.
- Add the `System.Text.Json` package reference to the project file with reference supporting project's target framework (always use tools to determine
  best package version  for the project, if you cannot find corresponding tool then try to determine version yourself).
- If projects use Central Package Management, update the `Directory.Packages.props` file to remove the Newtonsoft.Json package version in addition to 
  removing package reference from projects.
  When adding the `System.Text.Json` PackageReference, add it to affected project files without a version and add PackageVersion element to the 
  Directory.Packages.props file with the version that supports the project's target framework.

2. Update code files using Newtonsoft.Json in the selected projects (and in projects that depend on them since they could receive `Newtonsoft.Json` transitively):

- Find ***all*** code files in the selected projects (and in projects that depend on them since they could receive Newtonsoft.Json transitively).
  When doing search of code files that need changes, prefer calling search tools with `upgrade_` prefix if available. Also do pass project's root folder for all 
  selected projects or projects that depend on them.
- Update the code files that use `Newtonsoft.Json` to use `System.Text.Json` instead. You never should add placeholders when updating code, or remove any comments in the code files,
  you must keep the business logic as close as possible to the original code but use new API. When checking if code file needs to be updated, you should check for
  using statements, types and API from `System.Data.SqlClient` namespace (skip comments and string literal constants).
- Ensure that you replace all `Newtonsoft.Json` using statements with `System.Text.Json` using statements (always check if there are any other Newtonsoft.Json  
  API used in the file having any of the `Newtonsoft.Json` using statements; if no othe API detected, `Newtonsoft.Json` using statements should be just removed 
  instead of replaced). If there were no `Newtonsoft.Json` using statements in the file, do not add `System.Text.Json` using statements. 
- When replacing types you must ensure that you add using statements for them, since some types that lived in main `Newtonsoft.Json` namespace live in other namespaces  
  under `System.Text.Json`. For example, `Newtonsoft.Json.JsonPropertyAtribute` often replaced with `System.Text.Json.Serialization.JsonPropertyNameAttribute`, when that 
  happens using statement with `System.Text.Json.Serialization` needs to be added (unless you use fully qualified type name) 
- If you see some code that really cannot be converted or will have potential behavior changes at runtime, remember files and code lines where it 
  happens at the end of the migration process you will generate a report markdown file and list all follow up steps user would have to do..
 
3. Validate that all places where `Newtonsoft.Json` was used are migrated. To do that search for `Newtonsoft.Json` in all affected projects and projects that depend 
   on them again and if still see any `Newtonsoft.Json` presence go back to step 2. Steps 2 and 3 should be repeated until you see no `Newtonsoft.Json`.

4. Build all modified projects to ensure that they compile without errors. If there are any build errors, you must fix them all yourself one by one and 
   don't stop until all errors are fixed.

5. Generate the report file under `<solution root>\.github folder`, the file name should be `NewtonsoftJsonToSystemTextJsonReport.md`, it is highly important that
   you generate report when migration complete. Report should contain: 
     - all project dependencies changes (mention what was changed, added or removed)
     - all code files that were changed (mention what was changed in the file, if it was not changed, just mention that the file was not changed)
     - all cases where you could not convert the code because of unsupported features and you were unable to find a workaround
     - all behavioral changes that have to be verified at runtime
     - all follow up steps that user would have to do in the report markdown file

## Detailed information about differences in Newtonsoft.Json and System.Text.Json

The System.Text.Json namespace provides functionality for serializing to and deserializing from JavaScript Object Notation (JSON). The System.Text.Json library is included in the runtime for .NET Core 3.1 and later versions. For other target frameworks, install the System.Text.Json NuGet package. The package supports:

.NET Standard 2.0 and later versions
.NET Framework 4.6.2 and later versions
.NET Core 2.0, 2.1, and 2.2

System.Text.Json focuses primarily on performance, security, and standards compliance. It has some key differences in default behavior and doesn't aim to have feature parity with Newtonsoft.Json. For some scenarios, System.Text.Json currently has no built-in functionality, but there are recommended workarounds. For other scenarios, workarounds are impractical.

The System.Text.Json team is investing in adding the features that are most often requested. If your application depends on a missing feature, consider filing an issue in the dotnet/runtime GitHub repository to find out if support for your scenario can be added.

Most of this article is about how to use the JsonSerializer API, but it also includes guidance on how to use the JsonDocument (which represents the Document Object Model or DOM), Utf8JsonReader, and Utf8JsonWriter types.

In Visual Basic, you can't use Utf8JsonReader, which also means you can't write custom converters. Most of the workarounds presented here require that you write custom converters. You can write a custom converter in C# and register it in a Visual Basic project. For more information, see Visual Basic support.

Table of differences
The following table lists Newtonsoft.Json features and System.Text.Json equivalents. The equivalents fall into the following categories:

| Newtonsoft.Json feature                                      | System.Text.Json equivalent                                      |
|--------------------------------------------------------------|------------------------------------------------------------------|
| Case-insensitive deserialization by default                  | PropertyNameCaseInsensitive global setting                       |
| Camel-case property names                                    | PropertyNamingPolicy global setting                              |
| Snake-case property names                                    | Snake case naming policy                                         |
| Minimal character escaping                                   | Strict character escaping, configurable                          |
| NullValueHandling.Ignore global setting                      | DefaultIgnoreCondition global option                             |
| Allow comments                                               | ReadCommentHandling global setting                               |
| Allow trailing commas                                        | AllowTrailingCommas global setting                               |
| Custom converter registration                                | Order of precedence differs                                      |
| Default maximum depth 64, configurable                       | Default maximum depth 64, configurable                           |
| PreserveReferencesHandling global setting                    | ReferenceHandling global setting                                 |
| Serialize or deserialize numbers in quotes                   | NumberHandling global setting, [JsonNumberHandling] attribute    |
| Deserialize to immutable classes and structs                 | JsonConstructor, C# 9 Records                                    |
| Support for fields                                           | IncludeFields global setting, [JsonInclude] attribute            |
| DefaultValueHandling global setting                          | DefaultIgnoreCondition global setting                            |
| NullValueHandling setting on [JsonProperty]                  | JsonIgnore attribute                                             |
| DefaultValueHandling setting on [JsonProperty]               | JsonIgnore attribute                                             |
| Deserialize Dictionary with non-string key                   | Supported                                                        |
| Support for non-public property setters and getters          | JsonInclude attribute                                            |
| [JsonConstructor] attribute                                  | [JsonConstructor] attribute                                      |
| ReferenceLoopHandling global setting                         | ReferenceHandling global setting                                 |
| Callbacks                                                    | Callbacks                                                        |
| NaN, Infinity, -Infinity                                     | Supported                                                        |
| Required setting on [JsonProperty] attribute                 | [JsonRequired] attribute and C# required modifier                |
| DefaultContractResolver to ignore properties                 | DefaultJsonTypeInfoResolver class                                |
| Polymorphic serialization                                    | [JsonDerivedType] attribute                                      |
| Polymorphic deserialization                                  | Type discriminator on [JsonDerivedType] attribute                |
| Deserialize string enum value                                | Deserialize string enum values                                   |
| MissingMemberHandling global setting                         | Handle missing members                                           |
| Populate properties without setters                          | Populate properties without setters                              |
| ObjectCreationHandling global setting                        | Reuse rather than replace properties                             |
| Support for a broad range of types                           | Some types require custom converters                             |
| Deserialize inferred type to object properties               | Not supported, workaround, sample                                |
| Deserialize JSON null literal to non-nullable value types    | Not supported, workaround, sample                                |
| DateTimeZoneHandling, DateFormatString settings              | Not supported, workaround, sample                                |
| JsonConvert.PopulateObject method                            | Not supported, workaround                                        |
| Support for System.Runtime.Serialization attributes          | Not supported, workaround, sample                                |
| JsonObjectAttribute                                          | Not supported, workaround                                        |
| Allow property names without quotes                          | Not supported by design                                          |
| Allow single quotes around string values                     | Not supported by design                                          |
| Allow non-string JSON values for string properties           | Not supported by design                                          |
| TypeNameHandling.All global setting                          | Not supported by design                                          |
| Support for JsonPath queries                                 | Not supported                                                    |
| Configurable limits                                          | Not supported                                                    |


This is not an exhaustive list of Newtonsoft.Json features. The list includes many of the scenarios that have been requested in GitHub issues or StackOverflow posts. 

### Differences in default behavior
System.Text.Json is strict by default and avoids any guessing or interpretation on the caller's behalf, emphasizing deterministic behavior. The library is intentionally designed this way for performance and security. Newtonsoft.Json is flexible by default. This fundamental difference in design is behind many of the following specific differences in default behavior.

### Case-insensitive deserialization
During deserialization, Newtonsoft.Json does case-insensitive property name matching by default. The System.Text.Json default is case-sensitive, which gives better performance since it's doing an exact match. For information about how to do case-insensitive matching, see Case-insensitive property matching.

If you're using System.Text.Json indirectly by using ASP.NET Core, you don't need to do anything to get behavior like Newtonsoft.Json. ASP.NET Core specifies the settings for camel-casing property names and case-insensitive matching when it uses System.Text.Json.

ASP.NET Core also enables deserializing quoted numbers by default.

### Minimal character escaping
During serialization, Newtonsoft.Json is relatively permissive about letting characters through without escaping them. That is, it doesn't replace them with \uxxxx where xxxx is the character's code point. Where it does escape them, it does so by emitting a \ before the character (for example, " becomes \"). System.Text.Json escapes more characters by default to provide defense-in-depth protections against cross-site scripting (XSS) or information-disclosure attacks and does so by using the six-character sequence. System.Text.Json escapes all non-ASCII characters by default, so you don't need to do anything if you're using StringEscapeHandling.EscapeNonAscii in Newtonsoft.Json. System.Text.Json also escapes HTML-sensitive characters, by default. For information about how to override the default System.Text.Json behavior, see Customize character encoding.

### Comments
During deserialization, Newtonsoft.Json ignores comments in the JSON by default. The System.Text.Json default is to throw exceptions for comments because the RFC 8259 specification doesn't include them. For information about how to allow comments, see Allow comments and trailing commas.

### Trailing commas
During deserialization, Newtonsoft.Json ignores trailing commas by default. It also ignores multiple trailing commas (for example, [{"Color":"Red"},{"Color":"Green"},,]). The System.Text.Json default is to throw exceptions for trailing commas because the RFC 8259 specification doesn't allow them. For information about how to make System.Text.Json accept them, see Allow comments and trailing commas. There's no way to allow multiple trailing commas.

### Converter registration precedence
The Newtonsoft.Json registration precedence for custom converters is as follows:

Attribute on property
Attribute on type
Converters collection
This order means that a custom converter in the Converters collection is overridden by a converter that is registered by applying an attribute at the type level. Both of those registrations are overridden by an attribute at the property level.

The System.Text.Json registration precedence for custom converters is different:

Attribute on property
Converters collection
Attribute on type
The difference here is that a custom converter in the Converters collection overrides an attribute at the type level. The intention behind this order of precedence is to make run-time changes override design-time choices. There's no way to change the precedence.

For more information about custom converter registration, see Register a custom converter.

Maximum depth
The latest version of Newtonsoft.Json has a maximum depth limit of 64 by default. System.Text.Json also has a default limit of 64, and it's configurable by setting JsonSerializerOptions.MaxDepth.

If you're using System.Text.Json indirectly by using ASP.NET Core, the default maximum depth limit is 32. The default value is the same as for model binding and is set in the JsonOptions class.

JSON strings (property names and string values)
During deserialization, Newtonsoft.Json accepts property names surrounded by double quotes, single quotes, or without quotes. It accepts string values surrounded by double quotes or single quotes. For example, Newtonsoft.Json accepts the following JSON:

```json
{
  "name1": "value",
  'name2': "value",
  name3: 'value'
}
```

System.Text.Json only accepts property names and string values in double quotes because that format is required by the RFC 8259 specification and is the only format considered valid JSON.

A value enclosed in single quotes results in a JsonException with the following message:

```
Output
''' is an invalid start of a value.
```
Non-string values for string properties
Newtonsoft.Json accepts non-string values, such as a number or the literals true and false, for deserialization to properties of type string. Here's an example of JSON that Newtonsoft.Json successfully deserializes to the following class:

```json
{
  "String1": 1,
  "String2": true,
  "String3": false
}
```

```csharp
public class ExampleClass
{
    public string String1 { get; set; }
    public string String2 { get; set; }
    public string String3 { get; set; }
}
```

System.Text.Json doesn't deserialize non-string values into string properties. A non-string value received for a string field results in a JsonException with the following message:

Output

Copy
The JSON value could not be converted to System.String.
Scenarios using JsonSerializer
Some of the following scenarios aren't supported by built-in functionality, but workarounds are possible. The workarounds are custom converters, which may not provide complete parity with Newtonsoft.Json functionality. For some of these, sample code is provided as examples. If you rely on these Newtonsoft.Json features, migration will require modifications to your .NET object models or other code changes.

For some of the following scenarios, workarounds are not practical or possible. If you rely on these Newtonsoft.Json features, migration will not be possible without significant changes.

Allow or write numbers in quotes
Newtonsoft.Json can serialize or deserialize numbers represented by JSON strings (surrounded by quotes). For example, it can accept: {"DegreesCelsius":"23"} instead of {"DegreesCelsius":23}. To enable that behavior in System.Text.Json, set JsonSerializerOptions.NumberHandling to WriteAsString or AllowReadingFromString, or use the [JsonNumberHandling] attribute.

If you're using System.Text.Json indirectly by using ASP.NET Core, you don't need to do anything to get behavior like Newtonsoft.Json. ASP.NET Core specifies web defaults when it uses System.Text.Json, and web defaults allow quoted numbers.

For more information, see Allow or write numbers in quotes.

Specify constructor to use when deserializing
The Newtonsoft.Json [JsonConstructor] attribute lets you specify which constructor to call when deserializing to a POCO.

System.Text.Json also has a [JsonConstructor] attribute. For more information, see Immutable types and Records.

Conditionally ignore a property
Newtonsoft.Json has several ways to conditionally ignore a property on serialization or deserialization:

DefaultContractResolver lets you select properties to include or ignore, based on arbitrary criteria.
The NullValueHandling and DefaultValueHandling settings on JsonSerializerSettings let you specify that all null-value or default-value properties should be ignored.
The NullValueHandling and DefaultValueHandling settings on the [JsonProperty] attribute let you specify individual properties that should be ignored when set to null or the default value.
System.Text.Json provides the following ways to ignore properties or fields while serializing:

The [JsonIgnore] attribute on a property causes the property to be omitted from the JSON during serialization.
The IgnoreReadOnlyProperties global option lets you ignore all read-only properties.
If you're including fields, the JsonSerializerOptions.IgnoreReadOnlyFields global option lets you ignore all read-only fields.
The DefaultIgnoreCondition global option lets you ignore all value type properties that have default values, or ignore all reference type properties that have null values.
In addition, in .NET 7 and later versions, you can customize the JSON contract to ignore properties based on arbitrary criteria. For more information, see Custom contracts.

Public and non-public fields
Newtonsoft.Json can serialize and deserialize fields as well as properties.

In System.Text.Json, use the JsonSerializerOptions.IncludeFields global setting or the [JsonInclude] attribute to include public fields when serializing or deserializing. For an example, see Include fields.

Preserve object references and handle loops
By default, Newtonsoft.Json serializes by value. For example, if an object contains two properties that contain a reference to the same Person object, the values of that Person object's properties are duplicated in the JSON.

Newtonsoft.Json has a PreserveReferencesHandling setting on JsonSerializerSettings that lets you serialize by reference:

An identifier metadata is added to the JSON created for the first Person object.
The JSON that is created for the second Person object contains a reference to that identifier instead of property values.
Newtonsoft.Json also has a ReferenceLoopHandling setting that lets you ignore circular references rather than throw an exception.

To preserve references and handle circular references in System.Text.Json, set JsonSerializerOptions.ReferenceHandler to Preserve. The ReferenceHandler.Preserve setting is equivalent to PreserveReferencesHandling = PreserveReferencesHandling.All in Newtonsoft.Json.

The ReferenceHandler.IgnoreCycles option has behavior similar to Newtonsoft.Json ReferenceLoopHandling.Ignore. One difference is that the System.Text.Json implementation replaces reference loops with the null JSON token instead of ignoring the object reference. For more information, see Ignore circular references.

Like the Newtonsoft.Json ReferenceResolver, the System.Text.Json.Serialization.ReferenceResolver class defines the behavior of preserving references on serialization and deserialization. Create a derived class to specify custom behavior. For an example, see GuidReferenceResolver.

Some related Newtonsoft.Json features aren't supported:

JsonPropertyAttribute.IsReference
JsonPropertyAttribute.ReferenceLoopHandling
For more information, see Preserve references and handle circular references.

Dictionary with non-string key
Both Newtonsoft.Json and System.Text.Json support collections of type Dictionary<TKey, TValue>. For information about supported key types, see Supported key types.

 Caution

Deserializing to a Dictionary<TKey, TValue> where TKey is typed as anything other than string could introduce a security vulnerability in the consuming application. For more information, see dotnet/runtime#4761.

Types without built-in support
System.Text.Json doesn't provide built-in support for the following types:

DataTable and related types (for more information, see Supported types)
ExpandoObject
TimeZoneInfo
BigInteger
DBNull
Type
ValueTuple and its associated generic types
Custom converters can be implemented for types that don't have built-in support.

Polymorphic serialization
Newtonsoft.Json automatically does polymorphic serialization. Starting in .NET 7, System.Text.Json supports polymorphic serialization through the JsonDerivedTypeAttribute attribute. For more information, see Serialize properties of derived classes.

Polymorphic deserialization
Newtonsoft.Json has a TypeNameHandling setting that adds type-name metadata to the JSON while serializing. It uses the metadata while deserializing to do polymorphic deserialization. Starting in .NET 7, System.Text.Json relies on type discriminator information to perform polymorphic deserialization. This metadata is emitted in the JSON and then used during deserialization to determine whether to deserialize to the base type or a derived type. For more information, see Serialize properties of derived classes.

To support polymorphic deserialization in older .NET versions, create a converter like the example in How to write custom converters.

Deserialize string enum values
By default, System.Text.Json doesn't support deserializing string enum values, whereas Newtonsoft.Json does. For example, the following code throws a JsonException:

```csharp
string json = "{ \"Text\": \"Hello\", \"Enum\": \"Two\" }";
var _ = JsonSerializer.Deserialize<MyObj>(json); // Throws exception.

class MyObj
{
    public string Text { get; set; } = "";
    public MyEnum Enum { get; set; }
}

enum MyEnum
{
    One,
    Two,
    Three
}
```

However, you can enable deserialization of string enum values by using the JsonStringEnumConverter converter. For more information, see Enums as strings.

Deserialization of object properties
When Newtonsoft.Json deserializes to Object, it:

Infers the type of primitive values in the JSON payload (other than null) and returns the stored string, long, double, boolean, or DateTime as a boxed object. Primitive values are single JSON values such as a JSON number, string, true, false, or null.
Returns a JObject or JArray for complex values in the JSON payload. Complex values are collections of JSON key-value pairs within braces ({}) or lists of values within brackets ([]). The properties and values within the braces or brackets can have additional properties or values.
Returns a null reference when the payload has the null JSON literal.
System.Text.Json stores a boxed JsonElement for both primitive and complex values whenever deserializing to Object, for example:

An object property.
An object dictionary value.
An object array value.
A root object.
However, System.Text.Json treats null the same as Newtonsoft.Json and returns a null reference when the payload has the null JSON literal in it.

To implement type inference for object properties, create a converter like the example in How to write custom converters.

Deserialize null to non-nullable type
Newtonsoft.Json doesn't throw an exception in the following scenario:

NullValueHandling is set to Ignore, and
During deserialization, the JSON contains a null value for a non-nullable value type.
In the same scenario, System.Text.Json does throw an exception. (The corresponding null-handling setting in System.Text.Json is JsonSerializerOptions.IgnoreNullValues = true.)

If you own the target type, the best workaround is to make the property in question nullable (for example, change int to int?).

Another workaround is to make a converter for the type, such as the following example that handles null values for DateTimeOffset types:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SystemTextJsonSamples
{
    public class DateTimeOffsetNullHandlingConverter : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null
                ? default
                : reader.GetDateTimeOffset();

        public override void Write(
            Utf8JsonWriter writer,
            DateTimeOffset dateTimeValue,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(dateTimeValue);
    }
}
```

Register this custom converter by using an attribute on the property or by adding the converter to the Converters collection.

Note: The preceding converter handles null values differently than Newtonsoft.Json does for POCOs that specify default values. For example, suppose the following code represents your target object:

```csharp
public class WeatherForecastWithDefault
{
    public WeatherForecastWithDefault()
    {
        Date = DateTimeOffset.Parse("2001-01-01");
        Summary = "No summary";
    }
    public DateTimeOffset Date { get; set; }
    public int TemperatureCelsius { get; set; }
    public string Summary { get; set; }
}
```
And suppose the following JSON is deserialized by using the preceding converter:

```json
{
  "Date": null,
  "TemperatureCelsius": 25,
  "Summary": null
}
```
After deserialization, the Date property has 1/1/0001 (default(DateTimeOffset)), that is, the value set in the constructor is overwritten. Given the same POCO and JSON, Newtonsoft.Json deserialization would leave 1/1/2001 in the Date property.

Deserialize to immutable classes and structs
Newtonsoft.Json can deserialize to immutable classes and structs because it can use constructors that have parameters.

In System.Text.Json, use the [JsonConstructor] attribute to specify use of a parameterized constructor. Records in C# 9 are also immutable and are supported as deserialization targets. For more information, see Immutable types and Records.

Required properties
In Newtonsoft.Json, you specify that a property is required by setting Required on the [JsonProperty] attribute. Newtonsoft.Json throws an exception if no value is received in the JSON for a property marked as required.

Starting in .NET 7, you can use the C# required modifier or the JsonRequiredAttribute attribute on a required property. System.Text.Json throws an exception if the JSON payload doesn't contain a value for the marked property. For more information, see Required properties.

Specify date format
Newtonsoft.Json provides several ways to control how properties of DateTime and DateTimeOffset types are serialized and deserialized:

The DateTimeZoneHandling setting can be used to serialize all DateTime values as UTC dates.
The DateFormatString setting and DateTime converters can be used to customize the format of date strings.
System.Text.Json supports ISO 8601-1:2019, including the RFC 3339 profile. This format is widely adopted, unambiguous, and makes round trips precisely. To use any other format, create a custom converter. For example, the following converters serialize and deserialize JSON that uses Unix epoch format with or without a time zone offset (values such as /Date(1590863400000-0700)/ or /Date(1590863400000)/):

```csharp
sealed class UnixEpochDateTimeOffsetConverter : System.Text.Json.Serialization.JsonConverter<DateTimeOffset>
{
    static readonly DateTimeOffset s_epoch = new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
    static readonly Regex s_regex = new(
        "^/Date\\(([+-]*\\d+)([+-])(\\d{2})(\\d{2})\\)/$",
        RegexOptions.CultureInvariant);

    public override DateTimeOffset Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string formatted = reader.GetString()!;
        Match match = s_regex.Match(formatted);

        if (
                !match.Success
                || !long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixTime)
                || !int.TryParse(match.Groups[3].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int hours)
                || !int.TryParse(match.Groups[4].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes))
        {
            throw new System.Text.Json.JsonException();
        }

        int sign = match.Groups[2].Value[0] == '+' ? 1 : -1;
        TimeSpan utcOffset = new(hours * sign, minutes * sign, 0);

        return s_epoch.AddMilliseconds(unixTime).ToOffset(utcOffset);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTimeOffset value,
        JsonSerializerOptions options)
    {
        long unixTime = value.ToUnixTimeMilliseconds();

        TimeSpan utcOffset = value.Offset;

        string formatted = string.Create(
            CultureInfo.InvariantCulture,
            $"/Date({unixTime}{(utcOffset >= TimeSpan.Zero ? "+" : "-")}{utcOffset:hhmm})/");

        writer.WriteStringValue(formatted);
    }
}
```

```csharp
sealed class UnixEpochDateTimeConverter : System.Text.Json.Serialization.JsonConverter<DateTime>
{
    static readonly DateTime s_epoch = new(1970, 1, 1, 0, 0, 0);
    static readonly Regex s_regex = new(
        "^/Date\\(([+-]*\\d+)\\)/$",
        RegexOptions.CultureInvariant);

    public override DateTime Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        string formatted = reader.GetString()!;
        Match match = s_regex.Match(formatted);

        if (
            !match.Success
            || !long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long unixTime))
        {
            throw new System.Text.Json.JsonException();
        }

        return s_epoch.AddMilliseconds(unixTime);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DateTime value,
        JsonSerializerOptions options)
    {
        long unixTime = (value - s_epoch).Ticks / TimeSpan.TicksPerMillisecond;

        string formatted = string.Create(CultureInfo.InvariantCulture, $"/Date({unixTime})/");
        writer.WriteStringValue(formatted);
    }
}
```
For more information, see DateTime and DateTimeOffset support in System.Text.Json.

Callbacks
Newtonsoft.Json lets you execute custom code at several points in the serialization or deserialization process:

OnDeserializing (when beginning to deserialize an object)
OnDeserialized (when finished deserializing an object)
OnSerializing (when beginning to serialize an object)
OnSerialized (when finished serializing an object)
System.Text.Json exposes the same notifications during serialization and deserialization. To use them, implement one or more of the following interfaces from the System.Text.Json.Serialization namespace:

IJsonOnDeserializing
IJsonOnDeserialized
IJsonOnSerializing
IJsonOnSerialized
Here's an example that checks for a null property and writes messages at start and end of serialization and deserialization:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Callbacks
{
    public class WeatherForecast : 
        IJsonOnDeserializing, IJsonOnDeserialized, 
        IJsonOnSerializing, IJsonOnSerialized
    {
        public DateTime Date { get; set; }
        public int TemperatureCelsius { get; set; }
        public string? Summary { get; set; }

        void IJsonOnDeserializing.OnDeserializing() => Console.WriteLine("\nBegin deserializing");
        void IJsonOnDeserialized.OnDeserialized()
        {
            Validate();
            Console.WriteLine("Finished deserializing");
        }
        void IJsonOnSerializing.OnSerializing()
        {
            Console.WriteLine("Begin serializing");
            Validate();
        }
        void IJsonOnSerialized.OnSerialized() => Console.WriteLine("Finished serializing");

        private void Validate()
        {
            if (Summary is null)
            {
                Console.WriteLine("The 'Summary' property is 'null'.");
            }
        }
    }

    public class Program
    {
        public static void Main()
        {
            var weatherForecast = new WeatherForecast
            {
                Date = DateTime.Parse("2019-08-01"),
                TemperatureCelsius = 25,
            };

            string jsonString = JsonSerializer.Serialize(weatherForecast);
            Console.WriteLine(jsonString);

            weatherForecast = JsonSerializer.Deserialize<WeatherForecast>(jsonString);
            Console.WriteLine($"Date={weatherForecast?.Date}");
            Console.WriteLine($"TemperatureCelsius={weatherForecast?.TemperatureCelsius}");
            Console.WriteLine($"Summary={weatherForecast?.Summary}");
        }
    }
}

// output:
//Begin serializing
//The 'Summary' property is 'null'.
//Finished serializing
//{"Date":"2019-08-01T00:00:00","TemperatureCelsius":25,"Summary":null}

//Begin deserializing
//The 'Summary' property is 'null'.
//Finished deserializing
//Date=8/1/2019 12:00:00 AM
//TemperatureCelsius = 25
//Summary=
```
The OnDeserializing code doesn't have access to the new POCO instance. To manipulate the new POCO instance at the start of deserialization, put that code in the POCO constructor.

Non-public property setters and getters
Newtonsoft.Json can use private and internal property setters and getters via the JsonProperty attribute.

System.Text.Json supports private and internal property setters and getters via the [JsonInclude] attribute. For sample code, see Non-public property accessors.

Populate existing objects
The JsonConvert.PopulateObject method in Newtonsoft.Json deserializes a JSON document to an existing instance of a class, instead of creating a new instance. System.Text.Json always creates a new instance of the target type by using the default public parameterless constructor. Custom converters can deserialize to an existing instance.

Reuse rather than replace properties
Starting in .NET 8, System.Text.Json supports reusing initialized properties rather than replacing them. There are some differences in behavior, which you can read about in the API proposal.

For more information, see Populate initialized properties.

Populate properties without setters
Starting in .NET 8, System.Text.Json supports populating properties, including those that don't have a setter. For more information, see Populate initialized properties.

Snake case naming policy
System.Text.Json includes a built-in naming policy for snake case. However, there are some behavior differences with Newtonsoft.Json for some inputs. The following table shows some of these differences when converting input using the JsonNamingPolicy.SnakeCaseLower policy.

Input	Newtonsoft.Json result	System.Text.Json result
"AB1"	"a_b1"	"ab1"
"SHA512Managed"	"sh_a512_managed"	"sha512_managed"
"abc123DEF456"	"abc123_de_f456"	"abc123_def456"
"KEBAB-CASE"	"keba_b-_case"	"kebab-case"
System.Runtime.Serialization attributes
System.Runtime.Serialization attributes such as DataContractAttribute, DataMemberAttribute, and IgnoreDataMemberAttribute let you define a data contract. A data contract is a formal agreement between a service and a client that abstractly describes the data to be exchanged. The data contract precisely defines which properties are serialized for exchange.

System.Text.Json doesn't have built-in support for these attributes. However, starting in .NET 7, you can use a custom type resolver to add support. For a sample, see ZCS.DataContractResolver.

Octal numbers
Newtonsoft.Json treats numbers with a leading zero as octal numbers. System.Text.Json doesn't allow leading zeroes because the RFC 8259 specification doesn't allow them.

Handle missing members
If the JSON that's being deserialized includes properties that are missing in the target type, Newtonsoft.Json can be configured to throw exceptions. By default, System.Text.Json ignores extra properties in the JSON, except when you use the [JsonExtensionData] attribute.

In .NET 8 and later versions, you can set your preference for whether to skip or disallow unmapped JSON properties using one of the following means:

Apply the JsonUnmappedMemberHandlingAttribute attribute to the type you're deserializing to.
To set your preference globally, set the JsonSerializerOptions.UnmappedMemberHandling property. Or, for source generation, set the JsonSourceGenerationOptionsAttribute.UnmappedMemberHandling property and apply the attribute to your JsonSerializerContext class.
Customize the JsonTypeInfo.UnmappedMemberHandling property.
JsonObjectAttribute
Newtonsoft.Json has an attribute, JsonObjectAttribute, that can be applied at the type level to control which members are serialized, how null values are handled, and whether all members are required. System.Text.Json has no equivalent attribute that can be applied on a type. For some behaviors, such as null value handling, you can either configure the same behavior on the global JsonSerializerOptions or individually on each property.

Consider the following example that uses Newtonsoft.Json.JsonObjectAttribute to specify that all null properties should be ignored:

```csarp
[JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
public class Person { ... }
```

In System.Text.Json, you can set the behavior for all types and properties:

```csharp
JsonSerializerOptions options = new()
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
};

string json = JsonSerializer.Serialize<Person>(person, options);
```

Or you can set the behavior on each property separately:

```csharp
public class Person
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Age { get; set; }
}
```

Next, consider the following example that uses Newtonsoft.Json.JsonObjectAttribute to specify that all member properties must be present in the JSON:

```csharp
[JsonObject(ItemRequired = Required.Always)]
public class Person { ... }
```

You can achieve the same behavior in System.Text.Json by adding the C# required modifier or the JsonRequiredAttribute to each property. For more information, see Required properties.

```csharp
public class Person
{
    [JsonRequired]
    public string? Name { get; set; }

    public required int? Age { get; set; }
}
```

### TraceWriter
Newtonsoft.Json lets you debug by using a TraceWriter to view logs that are generated by serialization or deserialization. System.Text.Json doesn't do logging.

### JsonDocument and JsonElement compared to JToken (like JObject, JArray)
System.Text.Json.JsonDocument provides the ability to parse and build a read-only Document Object Model (DOM) from existing JSON payloads. The DOM provides random access to data in a JSON payload. The JSON elements that compose the payload can be accessed via the JsonElement type. The JsonElement type provides APIs to convert JSON text to common .NET types. JsonDocument exposes a RootElement property.

Starting in .NET 6, you can parse and build a mutable DOM from existing JSON payloads by using the JsonNode type and other types in the System.Text.Json.Nodes namespace. For more information, see Use JsonNode.

JsonDocument is IDisposable
JsonDocument builds an in-memory view of the data into a pooled buffer. Therefore, unlike JObject or JArray from Newtonsoft.Json, the JsonDocument type implements IDisposable and needs to be used inside a using block. For more information, see JsonDocument is IDisposable.

JsonDocument is read-only
The System.Text.Json DOM can't add, remove, or modify JSON elements. It's designed this way for performance and to reduce allocations for parsing common JSON payload sizes (that is, < 1 MB).

JsonElement is a union struct
JsonDocument exposes the RootElement as a property of type JsonElement, which is a union struct type that encompasses any JSON element. Newtonsoft.Json uses dedicated hierarchical types like JObject, JArray, JToken, and so forth. JsonElement is what you can search and enumerate over, and you can use JsonElement to materialize JSON elements into .NET types.

Starting in .NET 6, you can use JsonNode type and types in the System.Text.Json.Nodes namespace that correspond to JObject, JArray, and JToken. For more information, see Use JsonNode.

How to search a JsonDocument and JsonElement for sub-elements
Searches for JSON tokens using JObject or JArray from Newtonsoft.Json tend to be relatively fast because they're lookups in some dictionary. By comparison, searches on JsonElement require a sequential search of the properties and hence are relatively slow (for example when using TryGetProperty). System.Text.Json is designed to minimize initial parse time rather than lookup time. For more information, see How to search a JsonDocument and JsonElement for sub-elements.

Utf8JsonReader vs. JsonTextReader
System.Text.Json.Utf8JsonReader is a high-performance, low allocation, forward-only reader for UTF-8 encoded JSON text, read from a ReadOnlySpan<byte> or ReadOnlySequence<byte>. The Utf8JsonReader is a low-level type that can be used to build custom parsers and deserializers.

Utf8JsonReader is a ref struct
The JsonTextReader in Newtonsoft.Json is a class. The Utf8JsonReader type differs in that it's a ref struct. For more information, see ref struct limitations for Utf8JsonReader.

Read null values into nullable value types
Newtonsoft.Json provides APIs that return Nullable<T>, such as ReadAsBoolean, which handles a Null TokenType for you by returning a bool?. The built-in System.Text.Json APIs return only non-nullable value types. For more information, see Read null values into nullable value types.

Multi-target for reading JSON
If you need to continue to use Newtonsoft.Json for certain target frameworks, you can multi-target and have two implementations. However, this is not trivial and would require some #ifdefs and source duplication. One way to share as much code as possible is to create a ref struct wrapper around Utf8JsonReader and Newtonsoft.Json.JsonTextReader. This wrapper would unify the public surface area while isolating the behavioral differences. This lets you isolate the changes mainly to the construction of the type, along with passing the new type around by reference. This is the pattern that the Microsoft.Extensions.DependencyModel library follows:

UnifiedJsonReader.JsonTextReader.cs
UnifiedJsonReader.Utf8JsonReader.cs
Utf8JsonWriter vs. JsonTextWriter
System.Text.Json.Utf8JsonWriter is a high-performance way to write UTF-8 encoded JSON text from common .NET types like String, Int32, and DateTime. The writer is a low-level type that can be used to build custom serializers.

Write raw values
Newtonsoft.Json has a WriteRawValue method that writes raw JSON where a value is expected. System.Text.Json has a direct equivalent: Utf8JsonWriter.WriteRawValue. For more information, see Write raw JSON.

Customize JSON format
JsonTextWriter includes the following settings, for which Utf8JsonWriter has no equivalent:

QuoteChar - Specifies the character to use to surround string values. Utf8JsonWriter always uses double quotes.
QuoteName - Specifies whether or not to surround property names with quotes. Utf8JsonWriter always surrounds them with quotes.
Starting in .NET 9, you can customize the indentation character and size for Utf8JsonWriter using options exposed by the JsonWriterOptions struct:

JsonWriterOptions.IndentCharacter
JsonWriterOptions.IndentSize
There are no workarounds that would let you customize the JSON produced by Utf8JsonWriter in these ways.

Write Timespan, Uri, or char values
JsonTextWriter provides WriteValue methods for TimeSpan, Uri, and char values. Utf8JsonWriter doesn't have equivalent methods. Instead, format these values as strings (by calling ToString(), for example) and call WriteStringValue.

Multi-target for writing JSON
If you need to continue to use Newtonsoft.Json for certain target frameworks, you can multi-target and have two implementations. However, this is not trivial and would require some #ifdefs and source duplication. One way to share as much code as possible is to create a wrapper around Utf8JsonWriter and Newtonsoft.Json.JsonTextWriter. This wrapper would unify the public surface area while isolating the behavioral differences. This lets you isolate the changes mainly to the construction of the type. Microsoft.Extensions.DependencyModel library follows:

UnifiedJsonWriter.JsonTextWriter.cs
UnifiedJsonWriter.Utf8JsonWriter.cs
TypeNameHandling.All not supported
The decision to exclude TypeNameHandling.All-equivalent functionality from System.Text.Json was intentional. Allowing a JSON payload to specify its own type information is a common source of vulnerabilities in web applications. In particular, configuring Newtonsoft.Json with TypeNameHandling.All allows the remote client to embed an entire executable application within the JSON payload itself, so that during deserialization the web application extracts and runs the embedded code. For more information, see Friday the 13th JSON attacks PowerPoint and Friday the 13th JSON attacks details.

JSON Path queries not supported
The JsonDocument DOM doesn't support querying by using JSON Path.

In a JsonNode DOM, each JsonNode instance has a GetPath method that returns a path to that node. But there is no built-in API to handle queries based on JSON Path query strings.

For more information, see the dotnet/runtime #31068 GitHub issue.

Some limits not configurable
System.Text.Json sets limits that can't be changed for some values, such as the maximum token size in characters (166 MB) and in base 64 (125 MB). For more information, see JsonConstants in the source code and GitHub issue dotnet/runtime #39953.

NaN, Infinity, -Infinity
Newtonsoft parses NaN, Infinity, and -Infinity JSON string tokens. With System.Text.Json, use JsonNumberHandling.AllowNamedFloatingPointLiterals. For information about how to use this setting, see Allow or write numbers in quotes.
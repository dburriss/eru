namespace Eru.Adapters

open System
open System.Text.Json
open System.Text.Json.Serialization

type OptionConverterFactory() =
    inherit JsonConverterFactory()

    override _.CanConvert(t) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    override _.CreateConverter(t, _options) =
        let inner       = t.GetGenericArguments().[0]
        let converterTy = typedefof<OptionConverter<_>>.MakeGenericType inner
        Activator.CreateInstance converterTy :?> JsonConverter

and OptionConverter<'T>() =
    inherit JsonConverter<'T option>()

    override _.Read(reader, _t, options) =
        if reader.TokenType = JsonTokenType.Null then None
        else Some (JsonSerializer.Deserialize<'T>(&reader, options))

    override _.Write(writer, value, options) =
        match value with
        | None   -> writer.WriteNullValue()
        | Some v -> JsonSerializer.Serialize(writer, v, options)

module Serialization =

    let options =
        let opts =
            JsonSerializerOptions(
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented        = true)
        opts.Converters.Add(OptionConverterFactory())
        opts

    let deserialize<'T> (json: string) : Result<'T, string> =
        try Ok (JsonSerializer.Deserialize<'T>(json, options))
        with ex -> Error ex.Message

    let serialize<'T> (value: 'T) : string =
        JsonSerializer.Serialize(value, options)

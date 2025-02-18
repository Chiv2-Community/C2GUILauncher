using DiscriminatedUnions;
using LanguageExt;
using log4net;
using System.Text.Json;

namespace UnchainedLauncher.Core.Utilities {


    public static class TypedJsonSerializer {
        /// <summary>
        /// Uses reflection to derive an ISerializer<T>, leveraging System.Text.Json and using the options defined in
        /// JsonHelpers.
        /// </summary>
        public static ISerializer<T> Derive<T>() =>
            new Serializer<T>(JsonHelpers.Serialize);
    }


    public static class TypedJsonDeserializer {
        /// <summary>
        /// Uses reflection to derive an IDeserializer<T>, leveraging System.Text.Json and using the options defined in
        /// JsonHelpers.
        /// </summary>
        public static IDeserializer<T> Derive<T>() =>
            new Deserializer<T>(JsonHelpers.Deserialize<T>);
    }

    public static class TypedJsonCodec {
        /// <summary>
        /// Uses reflection to derive an ICodec<T>, leveraging System.Text.Json and using the options defined in
        /// JsonHelpers.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static ICodec<T> Derive<T>() => new Codec<T>(
            TypedJsonSerializer.Derive<T>(),
            TypedJsonDeserializer.Derive<T>()
        );
    }

    public class DerivedJsonCodec<TJson, T> : DerivedCodec<TJson, T> {
        public DerivedJsonCodec(Func<T, TJson> contramap, Func<TJson, T> map) : base(TypedJsonCodec.Derive<TJson>(), contramap, map) { }
    }

    public static class JsonHelpers {
        private static readonly ILog Logger = LogManager.GetLogger(nameof(JsonHelpers));

        private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions {
            Converters = { new UnionConverterFactory() },
            WriteIndented = true,
            IncludeFields = true,
        };

        /// <summary>
        /// Returns a composable deserialization result with the result and exception.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="json"></param>
        /// <returns></returns>
        public static DeserializationResult<T> Deserialize<T>(string json) {
            try {
                var result = JsonSerializer.Deserialize<T>(json, SerializerOptions);
                return new DeserializationResult<T>(result, null);
            }
            catch (JsonException e) {
                return new DeserializationResult<T>(default, e);
            }
        }

        public static string Serialize<T>(T obj) =>
            JsonSerializer.Serialize(obj, SerializerOptions);

    }

    public record DeserializationResult<T>(T? Result, Exception? Exception) {
        public bool Success => Result != null;

        /// <summary>
        /// If !Successful, run the function and return the result. 
        /// Combining the errors if this function also fails.
        /// </summary>
        /// <param name="recover"></param>
        /// <returns></returns>
        public DeserializationResult<T> RecoverWith(Func<Exception?, DeserializationResult<T>> recover) {
            if (Success) {
                return this;
            }

            var result = recover(Exception);

            result = CombineErrors(result);

            return result;
        }

        /// <summary>
        /// Functor map. Run a function to transform the result if successful.
        /// </summary>
        /// <typeparam name="T2"></typeparam>
        /// <param name="func"></param>
        /// <returns></returns>
        public DeserializationResult<T2> Select<T2>(Func<T, T2> func) {
            if (Success) {
                return new DeserializationResult<T2>(func(Result!), Exception);
            }

            return new DeserializationResult<T2>(default, Exception);
        }

        public DeserializationResult<T2> Map<T2>(Func<T, T2> func) => Select(func);

        public DeserializationResult<T2> Bind<T2>(Func<T, DeserializationResult<T2>> func) =>
          Result == null
            ? new DeserializationResult<T2>(default, Exception)
            : func(Result);

        private DeserializationResult<T> CombineErrors(DeserializationResult<T> other) {
            Exception? e = null;

            if (other.Exception != null && Exception != null) {
                e = new AggregateException(
                    "Failed to deserialize either of the provided types",
                    new List<Exception>() { other.Exception, Exception }
                );
            }
            else if (other.Exception != null) {
                e = other.Exception;
            }
            else if (Exception != null) {
                e = Exception;
            }

            return new DeserializationResult<T>(Success ? Result : other.Result, e);
        }

        public Try<T> ToTry() =>
            Success
                ? Prelude.Try<T>(Result!)
                : Prelude.TryFail<T>(Exception!);

        public Either<Exception, T> ToEither() =>
            Success
                ? Either<Exception, T>.Right(Result!)
                : Either<Exception, T>.Left(Exception!);
    }
}
using System.Collections;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace Server.Asp.Configuration;

public static class OptionsRegistration
{
    /// <summary>
    /// Binds a configuration section to <typeparamref name="T"/> and validates it (including nested objects and lists)
    /// when the host starts, so a wrong value stops the server with a readable message.
    /// </summary>
    public static IServiceCollection AddValidatedOptions<T>(this IServiceCollection services, string sectionName)
        where T : class
    {
        services.AddOptions<T>()
            .BindConfiguration(sectionName)
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<T>>(new RecursiveDataAnnotationsValidator<T>(sectionName));
        return services;
    }

    private sealed class RecursiveDataAnnotationsValidator<T>(string sectionName) : IValidateOptions<T> where T : class
    {
        public ValidateOptionsResult Validate(string? name, T options)
        {
            var errors = new List<string>();
            Collect(options, sectionName, errors);
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }

        private static void Collect(object instance, string path, List<string> errors)
        {
            var results = new List<ValidationResult>();
            Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
            foreach (var result in results)
            {
                var members = result.MemberNames.Any() ? string.Join(", ", result.MemberNames) : "";
                errors.Add($"{path}{(members.Length > 0 ? ":" + members : "")}: {result.ErrorMessage}");
            }

            foreach (var property in instance.GetType().GetProperties())
            {
                if (property.GetIndexParameters().Length > 0 || property.PropertyType == typeof(string))
                    continue;

                var value = property.GetValue(instance);
                switch (value)
                {
                    case null:
                        continue;
                    case IEnumerable items:
                        var index = 0;
                        foreach (var item in items)
                        {
                            if (item != null && IsOptionsType(item.GetType()))
                                Collect(item, $"{path}:{property.Name}:{index}", errors);
                            index++;
                        }
                        break;
                    default:
                        if (IsOptionsType(value.GetType()))
                            Collect(value, $"{path}:{property.Name}", errors);
                        break;
                }
            }
        }

        private static bool IsOptionsType(Type type) => type.IsClass && type != typeof(string) && type.Namespace?.StartsWith("Server.") == true;
    }
}

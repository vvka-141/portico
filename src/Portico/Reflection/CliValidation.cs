using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace Portico.Reflection;

/// <summary>
/// Runs <see cref="System.ComponentModel.DataAnnotations"/> validation against materialized option
/// and argument values, surfacing failures through <see cref="CliOptionMaterializationException"/>
/// so the existing usage-error path prints them. The idiom is deliberate: users already speak the
/// DataAnnotations vocabulary from MVC/EF; no new grammar to learn.
/// </summary>
internal static class CliValidation
{
    /// <summary>
    /// Validates a scalar value produced for <paramref name="parameter"/> against any
    /// <see cref="ValidationAttribute"/>s declared on the parameter itself. No-op when there are
    /// none. <paramref name="displayName"/> is the user-facing label embedded in the error message
    /// (e.g. <c>--count</c> for an option, <c>&lt;PATH&gt;</c> for an argument).
    /// </summary>
    public static void ValidateParameterValue(ParameterInfo parameter, object? value, string displayName)
    {
        var attributes = parameter.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray();
        if (attributes.Length == 0) return;
        Validate(value, attributes, displayName);
    }

    /// <summary>
    /// Validates a scalar value produced for <paramref name="property"/> against any
    /// <see cref="ValidationAttribute"/>s declared on the property itself. Used for bundle
    /// properties so per-property annotations fire before the whole-bundle pass.
    /// </summary>
    public static void ValidatePropertyValue(PropertyInfo property, object? value, string displayName)
    {
        var attributes = property.GetCustomAttributes<ValidationAttribute>(inherit: true).ToArray();
        if (attributes.Length == 0) return;
        Validate(value, attributes, displayName);
    }

    /// <summary>
    /// Runs cross-property validation on a materialized bundle when it implements
    /// <see cref="IValidatableObject"/>. Per-property attributes are already handled by
    /// <see cref="ValidatePropertyValue"/> (with CLI-friendly display names); going through
    /// <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}?)"/>
    /// here would either duplicate those errors or switch to raw property names in the output.
    /// </summary>
    public static void ValidateBundle(object bundle)
    {
        if (bundle is not IValidatableObject validatable) return;

        // IL2026: ValidationContext ctor uses reflection for display names;
        // CliApplication.Create/Run warn consumers about trimming incompatibility.
#pragma warning disable IL2026
        var ctx = new ValidationContext(bundle);
#pragma warning restore IL2026
        var results = validatable.Validate(ctx)
            .Where(r => r != ValidationResult.Success)
            .ToArray();
        if (results.Length == 0) return;

        var messages = results
            .Select(r => r.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToArray();

        throw new CliOptionMaterializationException(
            messages.Length == 0
                ? $"Validation failed for option bundle '{bundle.GetType().Name}'."
                : $"Validation failed for option bundle '{bundle.GetType().Name}': {string.Join("; ", messages)}");
    }

    private static void Validate(object? value, ValidationAttribute[] attributes, string displayName)
    {
#pragma warning disable IL2026
        var ctx = new ValidationContext(value ?? new object()) { DisplayName = displayName };
#pragma warning restore IL2026
        var results = new List<ValidationResult>();
        if (Validator.TryValidateValue(value!, ctx, results, attributes))
        {
            return;
        }

        var messages = results
            .Select(r => r.ErrorMessage)
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToArray();

        throw new CliOptionMaterializationException(
            messages.Length == 0
                ? $"Invalid value for '{displayName}'."
                : $"Invalid value for '{displayName}': {string.Join("; ", messages)}");
    }
}
